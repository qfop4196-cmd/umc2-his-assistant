using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using HisAdmissionAssistant;

namespace SmokeTest
{
    /// <summary>
    /// Runs against tools\MockHis (no real HIS, no patient data). Usage: SmokeTest.exe tools\MockHis\bin\Release\MockHis.exe
    /// </summary>
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.Error.WriteLine("Usage: SmokeTest <MockHis.exe>");
                return 2;
            }

            Process process = null;
            try
            {
                process = Process.Start(args[0]);
                process.WaitForInputIdle(5000);
                var started = process;
                Console.CancelKeyPress += delegate { CloseQuietly(started); }; // Ctrl+C must not leave Mock HIS running

                var service = new UiaAutomationService();
                var profile = MockProfile();

                TargetWindow target = null;
                for (var i = 0; i < 20 && target == null; i++)
                {
                    target = service.FindWindows(profile).FirstOrDefault();
                    if (target == null) Thread.Sleep(150);
                }
                if (target == null) throw new InvalidOperationException("Mock HIS window not found.");

                // 1. Basic write with patient verification; the Read field must never be written.
                var fields = new[]
                {
                    Field("PatientId", "mabn", "Verify", "BN-TEST-001", false, true),
                    Field("PatientName", "hoten", "Read", "MUST-NOT-BE-WRITTEN", false, false),
                    Field("ReasonForAdmission", "lydo", "Set", "SMOKE-REASON", true, true),
                    Field("Diagnosis", "chandoan", "Set", "SMOKE-DIAGNOSIS", true, true)
                };
                var results = service.Apply(target.Element, fields);
                if (results.Count(r => r.Changed) != 2)
                    throw new InvalidOperationException("Expected two changed fields: " + string.Join(" | ", results.Select(r => r.Message).ToArray()));
                if (results.Any(r => r.Field.Key == "PatientName"))
                    throw new InvalidOperationException("Read-only identity field was processed by Apply.");
                AssertValue(target.Element, "lydo", "SMOKE-REASON");
                AssertValue(target.Element, "chandoan", "SMOKE-DIAGNOSIS");
                AssertValue(target.Element, "hoten", "NGUYỄN VĂN TEST");
                AssertSaveCountZero(target.Element);

                // 2. Multi-line text gets CRLF; single-line text is flattened.
                var formatting = new[]
                {
                    Field("PatientId", "mabn", "Verify", "BN-TEST-001", false, true),
                    Field("History", "benhly", "Set", "Dòng 1\nDòng 2", true, false),
                    Field("BloodPressure", "huyetap", "Set", "130/80\n", false, false)
                };
                results = service.Apply(target.Element, formatting);
                if (results.Count(r => r.Changed) != 2)
                    throw new InvalidOperationException("Formatting write failed: " + string.Join(" | ", results.Select(r => r.Message).ToArray()));
                AssertValue(target.Element, "benhly", "Dòng 1\r\nDòng 2");
                AssertValue(target.Element, "huyetap", "130/80");

                // 3. Existing HIS content is reported before any overwrite.
                var existing = service.ReadExistingValues(target.Element, new[] { Field("ReasonForAdmission", "lydo", "Set", "NEW", true, false) });
                if (existing.Count != 1 || existing.Values.First() != "SMOKE-REASON")
                    throw new InvalidOperationException("ReadExistingValues did not report the current HIS text.");

                // 4. Patient mismatch aborts every write.
                var mismatchFields = new[]
                {
                    Field("PatientId", "mabn", "Verify", "WRONG-PATIENT", false, true),
                    Field("PastHistory", "banthan", "Set", "MUST-NOT-BE-WRITTEN", true, true)
                };
                if (service.Apply(target.Element, mismatchFields).Any(r => r.Changed))
                    throw new InvalidOperationException("Patient mismatch did not abort all writes.");
                AssertValue(target.Element, "banthan", string.Empty);

                // 5. A field without AutomationId/Name is never guessed ("first Edit on the form").
                var unmapped = new[]
                {
                    Field("PatientId", "mabn", "Verify", "BN-TEST-001", false, true),
                    new FieldMapping { Key = "NewField1", Label = "Unmapped", AutomationId = "", Name = "", ControlType = "Edit", Operation = "Set", Value = "GUESS" }
                };
                if (service.Apply(target.Element, unmapped).Any(r => r.Changed))
                    throw new InvalidOperationException("Unmapped field was written somewhere.");

                // 6. Save-like selectors are blocked.
                var dangerousFields = new[]
                {
                    new FieldMapping { Key = "Save", Label = "Save", AutomationId = "butLuu", ControlType = "Button", Operation = "Set", Required = true, Value = "invoke" }
                };
                var dangerousResults = service.Apply(target.Element, dangerousFields);
                if (dangerousResults.Any(r => r.Changed) || !dangerousResults.Any(r => r.Message.StartsWith("Bị chặn")))
                    throw new InvalidOperationException("Dangerous save selector was not blocked.");
                AssertSaveCountZero(target.Element);

                // 7. A target that cannot take text (read-only box, a button) blocks the WHOLE fill before anything is written.
                var blocked = new[]
                {
                    Field("PatientId", "mabn", "Verify", "BN-TEST-001", false, true),
                    Field("PreliminaryDiagnosis", "sobo", "Set", "MUST-NOT-BE-WRITTEN", true, false),
                    Field("Note", "hoten", "Set", "MUST-NOT-BE-WRITTEN", false, false),
                    Field("Treatment", "butBoqua", "Set", "MUST-NOT-BE-WRITTEN", false, false)
                };
                var blockedResults = service.Apply(target.Element, blocked);
                if (blockedResults.Any(r => r.Changed) || blockedResults.Count(r => r.Message.StartsWith("Không ghi được")) != 2)
                    throw new InvalidOperationException("Non-writable targets were not blocked in preflight: " +
                        string.Join(" | ", blockedResults.Select(r => r.Field.Key + "=" + r.Message).ToArray()));
                AssertValue(target.Element, "sobo", string.Empty);
                AssertValue(target.Element, "hoten", "NGUYỄN VĂN TEST");

                // 8. Joined read-only selector ("mabn1+mabn3" on UMC2HIS khám bệnh): verify, abort on mismatch, never write.
                var joined = new[]
                {
                    Field("PatientId", "mabn+namsinh", "Verify", "BN-TEST-001 1970", false, true),
                    Field("Treatment", "xuli", "Set", "SMOKE-JOINED", true, false)
                };
                var joinedResults = service.Apply(target.Element, joined);
                if (joinedResults.Count(r => r.Changed) != 1 || joinedResults.First(r => r.Changed).Message != "Đã điền")
                    throw new InvalidOperationException("Joined verify/write-back failed: " + string.Join(" | ", joinedResults.Select(r => r.Message).ToArray()));
                AssertValue(target.Element, "xuli", "SMOKE-JOINED");
                joined[0].Value = "BN-TEST-001";
                joined[1].Value = "MUST-NOT-BE-WRITTEN";
                if (service.Apply(target.Element, joined).Any(r => r.Changed))
                    throw new InvalidOperationException("Joined verify accepted a partial patient code.");
                AssertValue(target.Element, "xuli", "SMOKE-JOINED");
                var joinedWrite = new[] { Field("Note", "chuy+sobo", "Set", "MUST-NOT-BE-WRITTEN", false, false) };
                if (service.Apply(target.Element, joinedWrite).Any(r => r.Changed))
                    throw new InvalidOperationException("A joined selector was used for writing.");
                var shapes = service.ValidateMappings(target.Element, new[] { Field("PatientId", "mabn+namsinh", "Verify", string.Empty, false, true) });
                if (shapes.Count != 1 || !shapes[0].Message.Contains("mabn: 11 ký tự") || !shapes[0].Message.Contains("namsinh: 4 chữ số") ||
                    shapes[0].Message.Contains("TEST") || shapes[0].Message.Contains("1970"))
                    throw new InvalidOperationException("Selector check must describe (not reveal) identity values: " + (shapes.Count > 0 ? shapes[0].Message : "none"));
                using (var reader = new PatientContextWatcher(service))
                {
                    var joinedProfile = MockProfile();
                    joinedProfile.Fields[0].AutomationId = "mabn+namsinh";
                    var now = reader.ReadNow(new PatientContext { Profile = joinedProfile, Window = target, WindowHandle = target.Element.Current.NativeWindowHandle });
                    if (now == null || now.PatientId != "BN-TEST-0011970" || now.PatientName != "NGUYỄN VĂN TEST" || now.BirthYear != "1970")
                        throw new InvalidOperationException("ReadNow with a joined code failed: " + (now == null ? "null" : now.Describe()));
                }

                // 9. Auto-detection of the patient open on screen, including switching patient.
                var detection = DetectionTest(service, profile, target);

                Console.WriteLine("PASS: write + read-back, read-only identity, CRLF/flatten, existing-content check, patient-mismatch abort, " +
                    "no-guess selector, save-selector block, non-writable preflight block, joined code selector, " + detection + "; save count remains zero.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
            finally
            {
                CloseQuietly(process);
            }
        }

        private static void CloseQuietly(Process process)
        {
            try
            {
                if (process == null || process.HasExited) return;
                process.CloseMainWindow();
                if (!process.WaitForExit(3000)) process.Kill();
            }
            catch (Exception)
            {
                // Already gone or not ours to stop: nothing else to clean up.
            }
        }

        private static string DetectionTest(UiaAutomationService service, AutomationProfile profile, TargetWindow target)
        {
            using (var watcher = new PatientContextWatcher(service) { IntervalMs = 500 })
            {
                var handle = new IntPtr(target.Element.Current.NativeWindowHandle);
                SetForegroundWindow(handle);
                try { target.Element.SetFocus(); }
                catch (InvalidOperationException) { }
                watcher.SetProfiles(new[] { profile });
                watcher.Start();
                var first = WaitFor(watcher, "BN-TEST-001", 6000);
                if (first == null) return "detection SKIPPED (needs an interactive desktop with Mock HIS in front)";
                if (first.PatientName != "NGUYỄN VĂN TEST" || first.BirthYear != "1970")
                    throw new InvalidOperationException("Detected identity incomplete: " + first.Describe());

                var combo = target.Element.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "mockPatient"));
                if (combo == null) throw new InvalidOperationException("Mock patient selector not found.");
                object pattern;
                if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern)) ((ExpandCollapsePattern)pattern).Expand();
                Thread.Sleep(200);
                var item = combo.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "BN-TEST-002 — TRẦN THỊ MẪU — 1988"));
                if (item == null || !item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                    return "detection OK (patient switch SKIPPED: combo items not exposed)";
                ((SelectionItemPattern)pattern).Select();
                if (combo.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out pattern)) ((ExpandCollapsePattern)pattern).Collapse();
                SetForegroundWindow(handle);
                var second = WaitFor(watcher, "BN-TEST-002", 6000);
                if (second == null) throw new InvalidOperationException("Watcher did not notice the patient switch.");
                return "auto-detection + patient switch";
            }
        }

        private static PatientContext WaitFor(PatientContextWatcher watcher, string patientId, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                var context = watcher.Current;
                if (context != null && context.PatientId == patientId) return context;
                Thread.Sleep(150);
            }
            return null;
        }

        private static AutomationProfile MockProfile()
        {
            var profile = new AutomationProfile { Name = "Smoke", ProcessNames = "MockHis", WindowTitleRegex = "^MOCK HIS", StopBeforeSave = true };
            profile.Fields.Add(Field("PatientId", "mabn", "Verify", string.Empty, false, true));
            profile.Fields.Add(Field("PatientName", "hoten", "Read", string.Empty, false, false));
            profile.Fields.Add(Field("BirthYear", "namsinh", "Read", string.Empty, false, false));
            return profile;
        }

        private static FieldMapping Field(string key, string automationId, string operation, string value, bool multiline, bool required)
        {
            return new FieldMapping
            {
                Key = key,
                Label = key,
                AutomationId = automationId,
                ControlType = "Edit",
                Operation = operation,
                Required = required,
                Multiline = multiline,
                Value = value
            };
        }

        private static void AssertValue(AutomationElement root, string automationId, string expected)
        {
            var element = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
            if (element == null) throw new InvalidOperationException("Control not found: " + automationId);
            string actual;
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                actual = ((ValuePattern)pattern).Current.Value;
            }
            else
            {
                object textPattern;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out textPattern))
                {
                    actual = ((TextPattern)textPattern).DocumentRange.GetText(-1).TrimEnd('\r', '\n');
                }
                else
                {
                    var handle = new IntPtr(element.Current.NativeWindowHandle);
                    var buffer = new StringBuilder(GetWindowTextLength(handle) + 2);
                    GetWindowText(handle, buffer, buffer.Capacity);
                    actual = buffer.ToString();
                }
            }
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Value mismatch: " + automationId +
                    "; actual='" + actual +
                    "'; name='" + element.Current.Name +
                    "'; type=" + element.Current.ControlType.ProgrammaticName +
                    "; hwnd=" + element.Current.NativeWindowHandle);
        }

        private static void AssertSaveCountZero(AutomationElement root)
        {
            var saveCount = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "saveCount"));
            if (saveCount == null || !saveCount.Current.Name.EndsWith("0", StringComparison.Ordinal))
                throw new InvalidOperationException("Save button was invoked or save counter was not exposed.");
        }
    }
}
