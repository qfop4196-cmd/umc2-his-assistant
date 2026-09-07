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
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

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

                var service = new UiaAutomationService();
                var profile = new AutomationProfile
                {
                    Name = "Smoke",
                    ProcessNames = "MockHis",
                    WindowTitleRegex = "^MOCK HIS",
                    StopBeforeSave = true
                };
                profile.Fields.Add(new FieldMapping
                {
                    Key = "PatientId",
                    Label = "Patient ID",
                    AutomationId = "mabn",
                    ControlType = "Edit",
                    Operation = "Verify",
                    Required = true,
                    Value = "BN-TEST-001"
                });
                profile.Fields.Add(new FieldMapping
                {
                    Key = "ReasonForAdmission",
                    Label = "Reason",
                    AutomationId = "lydo",
                    ControlType = "Edit",
                    Operation = "Set",
                    Required = true,
                    Value = "SMOKE-REASON"
                });
                profile.Fields.Add(new FieldMapping
                {
                    Key = "Diagnosis",
                    Label = "Diagnosis",
                    AutomationId = "chandoan",
                    ControlType = "Edit",
                    Operation = "Set",
                    Required = true,
                    Value = "SMOKE-DIAGNOSIS"
                });

                TargetWindow target = null;
                for (var i = 0; i < 20 && target == null; i++)
                {
                    target = service.FindWindows(profile).FirstOrDefault();
                    if (target == null) Thread.Sleep(150);
                }
                if (target == null) throw new InvalidOperationException("Mock HIS window not found.");

                var results = service.Apply(target.Element, profile.Fields);
                if (results.Count(r => r.Changed) != 2)
                    throw new InvalidOperationException("Expected two changed fields: " + string.Join(" | ", results.Select(r => r.Message).ToArray()));

                AssertValue(target.Element, "lydo", "SMOKE-REASON");
                AssertValue(target.Element, "chandoan", "SMOKE-DIAGNOSIS");
                AssertSaveCountZero(target.Element);

                var mismatchFields = new[]
                {
                    new FieldMapping
                    {
                        Key = "PatientId", Label = "Patient ID", AutomationId = "mabn", ControlType = "Edit",
                        Operation = "Verify", Required = true, Value = "WRONG-PATIENT"
                    },
                    new FieldMapping
                    {
                        Key = "History", Label = "History", AutomationId = "benhly", ControlType = "Edit",
                        Operation = "Set", Required = true, Value = "MUST-NOT-BE-WRITTEN"
                    }
                };
                var mismatchResults = service.Apply(target.Element, mismatchFields);
                if (mismatchResults.Any(r => r.Changed))
                    throw new InvalidOperationException("Patient mismatch did not abort all writes.");
                AssertValue(target.Element, "benhly", string.Empty);

                var dangerousFields = new[]
                {
                    new FieldMapping
                    {
                        Key = "Save", Label = "Save", AutomationId = "butLuu", ControlType = "Button",
                        Operation = "Set", Required = true, Value = "invoke"
                    }
                };
                var dangerousResults = service.Apply(target.Element, dangerousFields);
                if (dangerousResults.Any(r => r.Changed) || !dangerousResults.Any(r => r.Message.StartsWith("Bị chặn")))
                    throw new InvalidOperationException("Dangerous save selector was not blocked.");
                AssertSaveCountZero(target.Element);

                Console.WriteLine("PASS: write, patient-mismatch abort, and save-selector block; save count remains zero.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
            finally
            {
                if (process != null && !process.HasExited)
                {
                    process.CloseMainWindow();
                    process.WaitForExit(3000);
                }
            }
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
