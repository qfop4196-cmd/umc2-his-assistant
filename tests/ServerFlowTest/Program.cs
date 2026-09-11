using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using HisAdmissionAssistant;

namespace ServerFlowTest
{
    /// <summary>
    /// End-to-end test of the intake server and the assistant's IntakeClient using fake data:
    /// patient submit → nurse setup/approve → device pairing → match → claim → complete, plus security checks.
    /// Usage: ServerFlowTest.exe <path-to-IntakeServer.exe>
    /// </summary>
    internal static class Program
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly CookieContainer Cookies = new CookieContainer();
        private static string staffUrl;
        private static string publicUrl;
        private static int checks;

        private static int Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine("Usage: ServerFlowTest <IntakeServer.exe>");
                return 2;
            }
            var dataDir = Path.Combine(Path.GetTempPath(), "umc2-flowtest-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            var staffPort = FreePort();
            var publicPort = FreePort();
            staffUrl = "http://localhost:" + staffPort;
            publicUrl = "http://localhost:" + publicPort;
            Process server = null;
            try
            {
                var isMono = Type.GetType("Mono.Runtime") != null;
                var info = new ProcessStartInfo
                {
                    FileName = isMono ? "mono" : args[0],
                    Arguments = (isMono ? "\"" + args[0] + "\" " : string.Empty) +
                        "--data \"" + dataDir + "\" --staff-port " + staffPort + " --public-port " + publicPort + " --bind localhost",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                server = Process.Start(info);
                var started = server;
                Console.CancelKeyPress += delegate { KillQuietly(started); }; // Ctrl+C must not leave the test server running
                server.OutputDataReceived += delegate { };
                server.ErrorDataReceived += delegate { };
                server.BeginOutputReadLine();
                server.BeginErrorReadLine();
                WaitForServer();

                // Patient submits (public port).
                var submit = Call("POST", publicUrl + "/api/public/intakes", new Dictionary<string, object>
                {
                    { "formVersion", "test" },
                    { "consent", true },
                    { "patient", new Dictionary<string, object> { { "fullName", "Bệnh Nhân Thử" }, { "birthDate", "1970-01-02" }, { "gender", "male" }, { "phone", "0900000001" } } },
                    { "answers", new Dictionary<string, object> { { "chiefComplaint", "Đau bụng" }, { "onsetValue", 2 }, { "onsetUnit", "ngày" } } }
                }, false);
                Expect(submit.Status == 201, "public submit returns 201");
                var code = Str(submit.Body, "code");
                Expect(code.Length == 7, "short code issued");

                // Missing consent is refused.
                var noConsent = Call("POST", publicUrl + "/api/public/intakes", new Dictionary<string, object>
                {
                    { "patient", new Dictionary<string, object> { { "fullName", "A B" }, { "birthDate", "1970" }, { "gender", "male" }, { "phone", "0900000002" } } },
                    { "answers", new Dictionary<string, object> { { "chiefComplaint", "x" } } }
                }, false);
                Expect(noConsent.Status == 400 && Str(noConsent.Body, "error") == "consent_required", "consent is mandatory");

                // Staff API is not reachable on the public port.
                Expect(Call("GET", publicUrl + "/api/staff/bootstrap", null, false).Status == 404, "staff API absent on public port");
                Expect(Call("GET", publicUrl + "/assets/staff.js", null, false).Status == 404, "staff assets absent on public port");

                // Nurse/admin setup + CSRF.
                var setupNoCsrf = Call("POST", staffUrl + "/api/setup", new Dictionary<string, object> { { "username", "admin" }, { "password", "Test12345" } }, false);
                Expect(setupNoCsrf.Status == 403, "state-changing staff call without X-UMC2 header is refused");
                var setup = Call("POST", staffUrl + "/api/setup", new Dictionary<string, object>
                {
                    { "username", "admin" }, { "displayName", "Admin Test" }, { "password", "Test12345" }, { "hospitalName", "BV Test" }
                }, true);
                Expect(setup.Status == 200, "first-run admin setup");
                Expect(Call("POST", staffUrl + "/api/setup", new Dictionary<string, object> { { "username", "x2" }, { "password", "Test12345" } }, true).Status == 409,
                    "setup cannot run twice");

                // Staff sees the pending intake and approves it with the HIS patient ID.
                var list = Call("GET", staffUrl + "/api/staff/intakes?status=pending", null, false);
                var items = (object[])((System.Collections.ArrayList)ToList(list.Body["items"])).ToArray();
                Expect(items.Length == 1, "one pending intake");
                var id = Str((Dictionary<string, object>)items[0], "id");
                var approve = Call("POST", staffUrl + "/api/staff/intakes/" + id + "/review", new Dictionary<string, object>
                {
                    { "action", "approve" },
                    { "hisPatientId", "bn-777" },
                    { "fields", new Dictionary<string, object> { { "ReasonForAdmission", "Đau bụng" }, { "History", "Dòng 1\nDòng 2" }, { "Pulse", "88" } } }
                }, true);
                Expect(approve.Status == 200 && Str(approve.Body, "status") == "approved", "nurse approves");

                // Pair a doctor's PC.
                var pairCode = Str(Call("POST", staffUrl + "/api/staff/devices/pair-code", new Dictionary<string, object>(), true).Body, "code");
                Expect(pairCode.Length == 6, "pair code created");
                var client = new IntakeClient();
                var paired = client.Pair(staffUrl, pairCode, "Test PC");
                Expect(!string.IsNullOrEmpty(paired.Token), "device paired");
                try
                {
                    client.Pair(staffUrl, pairCode, "Again");
                    Expect(false, "pair code is single-use");
                }
                catch (IntakeApiException ex)
                {
                    Expect(ex.Code == "invalid_code", "pair code is single-use");
                }
                client.BaseUrl = staffUrl;
                client.Token = paired.Token;
                Expect(Str((Dictionary<string, object>)client.Ping(), "deviceName") == "Test PC", "ping with device token");

                // Discovery (UDP) finds the server on this machine.
                var discovered = IntakeClient.Discover(1500);
                Expect(discovered.Any(d => d.Url.EndsWith(":" + staffPort)), "UDP discovery finds the server");

                // Match by the HIS patient ID currently open (case/format-insensitive).
                var matches = client.Match("BN777", string.Empty, string.Empty);
                Expect(matches.Count == 1 && matches[0].Score == 100 && matches[0].Fields["PatientId"] == "BN-777", "exact match by HIS patient ID");
                Expect(matches[0].Fields["History"] == "Dòng 1\nDòng 2", "fields delivered to the doctor's PC");
                Expect(client.Match("BN-778", "Bệnh Nhân Thử", "1970").Count == 0, "different HIS ID never matches even with same name");

                // Claim with the wrong patient on screen is refused; the right one succeeds.
                try
                {
                    client.Claim(matches[0].Id, "BN-778", false);
                    Expect(false, "claim refuses patient mismatch");
                }
                catch (IntakeApiException ex)
                {
                    Expect(ex.Code == "patient_mismatch", "claim refuses patient mismatch");
                }
                var claimed = client.Claim(matches[0].Id, "BN-777", false);
                Expect(claimed.Status == "claimed", "claim");
                client.Complete(matches[0].Id, "completed", "test", "BN-777", 3);
                var done = Call("GET", staffUrl + "/api/staff/intakes/" + id, null, false);
                Expect(Str(done.Body, "status") == "completed", "completed status visible to nurses");
                Expect(client.Match("BN-777", string.Empty, string.Empty).Count == 0, "completed intake no longer offered");

                // Revocation.
                var devices = (System.Collections.ArrayList)ToList(Call("GET", staffUrl + "/api/staff/devices", null, false).Body["items"]);
                var deviceId = Str((Dictionary<string, object>)devices[0], "id");
                Call("POST", staffUrl + "/api/staff/devices/" + deviceId + "/revoke", new Dictionary<string, object>(), true);
                try
                {
                    client.Ping();
                    Expect(false, "revoked device is refused");
                }
                catch (IntakeApiException ex)
                {
                    Expect(ex.IsUnauthorized, "revoked device is refused");
                }

                // Encryption at rest: no plaintext patient name in the data folder.
                var plaintextFound = Directory.GetFiles(Path.Combine(dataDir, "intakes"), "*.dat")
                    .Any(f => Encoding.UTF8.GetString(File.ReadAllBytes(f)).Contains("Thử"));
                Expect(!plaintextFound, "records are encrypted at rest");
                var logs = string.Join("\n", Directory.GetFiles(Path.Combine(dataDir, "logs")).Select(f => File.ReadAllText(f)).ToArray());
                Expect(!logs.Contains("Bệnh Nhân Thử") && !logs.Contains("0900000001"), "logs contain no patient identifiers");

                Console.WriteLine("PASS: " + checks + " checks (submit, consent, port isolation, CSRF, setup, approve, pairing, discovery, match, mismatch guard, claim/complete, revoke, encryption, clean logs).");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
            finally
            {
                KillQuietly(server);
                try { Directory.Delete(dataDir, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void KillQuietly(Process process)
        {
            try
            {
                if (process != null && !process.HasExited) process.Kill();
            }
            catch (Exception)
            {
                // Already exited.
            }
        }

        private sealed class Reply
        {
            public int Status;
            public Dictionary<string, object> Body;
        }

        private static void Expect(bool condition, string what)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Check failed: " + what);
        }

        private static void WaitForServer()
        {
            for (var i = 0; i < 60; i++)
            {
                try
                {
                    if (Call("GET", publicUrl + "/api/public/health", null, false).Status == 200) return;
                }
                catch (WebException)
                {
                }
                Thread.Sleep(250);
            }
            throw new InvalidOperationException("Server did not start.");
        }

        private static Reply Call(string method, string url, object body, bool csrf)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = method;
            request.CookieContainer = Cookies;
            request.Proxy = null;
            request.Timeout = 10000;
            if (csrf) request.Headers["X-UMC2"] = "1";
            if (body != null)
            {
                var bytes = Encoding.UTF8.GetBytes(Json.Serialize(body));
                request.ContentType = "application/json";
                request.ContentLength = bytes.Length;
                using (var s = request.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
            }
            HttpWebResponse response;
            try { response = (HttpWebResponse)request.GetResponse(); }
            catch (WebException ex)
            {
                response = ex.Response as HttpWebResponse;
                if (response == null) throw;
            }
            using (response)
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                var text = reader.ReadToEnd();
                var parsed = text.Length > 0 && text[0] == '{' ? Json.DeserializeObject(text) as Dictionary<string, object> : null;
                return new Reply { Status = (int)response.StatusCode, Body = parsed ?? new Dictionary<string, object>() };
            }
        }

        private static object ToList(object value)
        {
            var array = value as object[];
            return array != null ? new System.Collections.ArrayList(array) : value as System.Collections.ArrayList;
        }

        private static string Str(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : string.Empty;
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
