using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

namespace HisAdmissionAssistant
{
    /// <summary>The patient currently open on the HIS screen in the foreground (read-only observation).</summary>
    public sealed class PatientContext
    {
        public AutomationProfile Profile { get; set; }
        public TargetWindow Window { get; set; }
        public int WindowHandle { get; set; }
        public string PatientId { get; set; }
        public string PatientName { get; set; }
        public string BirthYear { get; set; }
        public DateTime DetectedUtc { get; set; }

        public string Key
        {
            get { return (Profile == null ? string.Empty : Profile.Name) + "|" + WindowHandle + "|" + (PatientId ?? string.Empty); }
        }

        /// <summary>Compact form for the always-on-top panel: "BN-240001 · TRẦN THỊ B (1988)".</summary>
        public string DescribeShort()
        {
            var text = PatientId;
            if (!string.IsNullOrWhiteSpace(PatientName)) text += " · " + PatientName.Trim();
            if (!string.IsNullOrWhiteSpace(BirthYear)) text += " (" + BirthYear.Trim() + ")";
            return text;
        }

        public string Describe()
        {
            var parts = new List<string> { "Mã BN " + PatientId };
            if (!string.IsNullOrWhiteSpace(PatientName)) parts.Add(PatientName.Trim());
            if (!string.IsNullOrWhiteSpace(BirthYear)) parts.Add("(" + BirthYear.Trim() + ")");
            return string.Join(" · ", parts.ToArray());
        }
    }

    public sealed class PatientContextEventArgs : EventArgs
    {
        public PatientContextEventArgs(PatientContext context)
        {
            Context = context;
        }

        public PatientContext Context { get; private set; }
    }

    /// <summary>
    /// Background observer: finds which HIS form/patient is in the foreground by reading the profile's PatientId (Verify)
    /// field and optional PatientName/BirthYear (Read) fields. Never writes. Uses cached UIA elements so steady-state
    /// polling costs a few ValuePattern reads (code, name, birth year — all re-read so a reused code box cannot hide a
    /// patient switch), and backs off when the foreground HIS window has no patient field. The patient code may be a
    /// joined selector ("mabn1+mabn3") when HIS shows it as a year prefix box plus a number box.
    /// </summary>
    public sealed class PatientContextWatcher : IDisposable
    {
        private readonly UiaAutomationService automation;
        private readonly object sync = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private List<AutomationProfile> profiles = new List<AutomationProfile>();
        private Thread thread;
        private volatile bool running;
        private volatile bool paused;
        private volatile bool forceSlowPath;
        private int generation;
        private PatientContext current;
        private int cachedHandle;
        private AutomationProfile cachedProfile;
        private AutomationElement[] cachedId;
        private AutomationElement[] cachedName;
        private AutomationElement[] cachedBirth;
        private int missHandle;
        private DateTime missUntilUtc;

        public PatientContextWatcher(UiaAutomationService automation)
        {
            this.automation = automation;
            IntervalMs = 1500;
        }

        public event EventHandler<PatientContextEventArgs> ContextChanged;

        public int IntervalMs { get; set; }

        public bool Paused
        {
            get { return paused; }
            set { paused = value; }
        }

        public bool IsRunning
        {
            get { return running; }
        }

        public PatientContext Current
        {
            get { lock (sync) return current; }
        }

        /// <summary>Replaces the profiles used for detection (only those with a PatientId selector are kept).</summary>
        public void SetProfiles(IEnumerable<AutomationProfile> source)
        {
            var usable = source.Where(p => p.Fields.Any(IsPatientIdField)).Select(CloneWithoutValues).ToList();
            lock (sync)
            {
                profiles = usable;
                cachedId = null;
                cachedHandle = 0;
            }
            DetectNow();
        }

        public void Start()
        {
            if (running) return;
            lock (sync)
            {
                current = null; // publish again even if the same patient is still open
                cachedId = null;
                cachedHandle = 0;
            }
            running = true;
            var myGeneration = Interlocked.Increment(ref generation);
            thread = new Thread(() => Loop(myGeneration)) { IsBackground = true, Name = "his-patient-watch" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            Interlocked.Increment(ref generation); // an old loop that is still inside Tick() exits afterwards
            wake.Set();
        }

        /// <summary>Asks for an immediate full re-scan (e.g. doctor pressed "Nhận diện lại").</summary>
        public void DetectNow()
        {
            forceSlowPath = true;
            missUntilUtc = DateTime.MinValue;
            wake.Set();
        }

        private void Loop(int myGeneration)
        {
            while (running && myGeneration == Thread.VolatileRead(ref generation))
            {
                if (!paused)
                {
                    try
                    {
                        Tick();
                    }
                    catch (ElementNotAvailableException)
                    {
                        cachedId = null;
                    }
                    catch (Exception)
                    {
                        // A transient UIA failure must never kill the watcher thread.
                        cachedId = null;
                    }
                }
                wake.WaitOne(Math.Max(500, IntervalMs));
            }
        }

        private void Tick()
        {
            List<AutomationProfile> snapshot;
            lock (sync) snapshot = profiles;
            if (snapshot.Count == 0)
            {
                Publish(null);
                return;
            }
            if (UiaAutomationService.AssistantIsForeground()) return; // doctor is using the assistant: keep last context

            var processNames = new HashSet<string>(snapshot.SelectMany(p => SplitNames(p.ProcessNames)), StringComparer.OrdinalIgnoreCase);
            var window = automation.ForegroundWindow(processNames);
            if (window == null) return; // another app in front: HIS still has the same patient open in the background
            var handle = window.Element.Current.NativeWindowHandle;
            var force = forceSlowPath;
            forceSlowPath = false;

            var cachedParts = cachedId;
            if (!force && handle == cachedHandle && cachedParts != null)
            {
                string id;
                if (IsShown(cachedParts[0]) && automation.TryReadParts(cachedParts, string.Empty, out id) && UiaAutomationService.IsPlausiblePatientId(id))
                {
                    // Publish() drops it when code, name and birth year are all unchanged.
                    Publish(Build(window, handle, cachedProfile, id.Trim(), cachedName, cachedBirth));
                    return;
                }
                cachedId = null;
            }

            if (!force && handle == missHandle && DateTime.UtcNow < missUntilUtc) return;

            foreach (var profile in snapshot)
            {
                if (!SplitNames(profile.ProcessNames).Contains(window.ProcessName, StringComparer.OrdinalIgnoreCase) && SplitNames(profile.ProcessNames).Any()) continue;
                if (!TitleMatches(profile, window.Title)) continue;
                var idField = profile.Fields.First(IsPatientIdField);
                var parts = automation.LocateParts(window.Element, idField);
                if (parts == null || !IsShown(parts[0])) continue;
                string id;
                if (!automation.TryReadParts(parts, string.Empty, out id) || !UiaAutomationService.IsPlausiblePatientId(id)) continue;
                cachedHandle = handle;
                cachedProfile = profile;
                cachedId = parts;
                cachedName = LocateRead(window.Element, profile, "PatientName");
                cachedBirth = LocateRead(window.Element, profile, "BirthYear") ?? LocateRead(window.Element, profile, "BirthDate");
                Publish(Build(window, handle, profile, id.Trim(), cachedName, cachedBirth));
                return;
            }

            missHandle = handle;
            missUntilUtc = DateTime.UtcNow.AddSeconds(4);

            // No patient field in this foreground window (e.g. an ICD lookup dialog of the same HIS process):
            // keep the current patient while its form is still open, instead of flapping to "no patient".
            var existing = Current;
            cachedParts = cachedId;
            if (existing != null && cachedParts != null && existing.Window != null && existing.Window.ProcessId == window.ProcessId)
            {
                string stillOpen;
                if (automation.TryReadParts(cachedParts, string.Empty, out stillOpen) && string.Equals((stillOpen ?? string.Empty).Trim(), existing.PatientId, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            cachedHandle = handle;
            cachedId = null;
            Publish(null);
        }

        /// <summary>
        /// Re-reads, right now and without the cache, the identity shown in the window of <paramref name="context"/>
        /// (used just before filling). Null when that window no longer shows a patient code.
        /// </summary>
        public PatientContext ReadNow(PatientContext context)
        {
            if (context == null || context.Profile == null || context.Window == null || context.Window.Element == null) return null;
            var profile = context.Profile;
            var idField = profile.Fields.FirstOrDefault(IsPatientIdField);
            if (idField == null) return null;
            var root = context.Window.Element;
            string id;
            var parts = automation.LocateParts(root, idField);
            if (parts == null || !automation.TryReadParts(parts, string.Empty, out id) || !UiaAutomationService.IsPlausiblePatientId(id)) return null;
            return Build(context.Window, context.WindowHandle, profile, id.Trim(),
                LocateRead(root, profile, "PatientName"),
                LocateRead(root, profile, "BirthYear") ?? LocateRead(root, profile, "BirthDate"));
        }

        private PatientContext Build(TargetWindow window, int handle, AutomationProfile profile, string id, AutomationElement[] name, AutomationElement[] birth)
        {
            string patientName = string.Empty, birthYear = string.Empty;
            if (name != null) automation.TryReadParts(name, " ", out patientName);
            if (birth != null)
            {
                string raw;
                if (automation.TryReadParts(birth, " ", out raw))
                {
                    var match = Regex.Match(raw ?? string.Empty, @"(19|20)\d{2}");
                    birthYear = match.Success ? match.Value : string.Empty;
                }
            }
            return new PatientContext
            {
                Profile = profile,
                Window = window,
                WindowHandle = handle,
                PatientId = id,
                PatientName = (patientName ?? string.Empty).Trim(),
                BirthYear = birthYear,
                DetectedUtc = DateTime.UtcNow
            };
        }

        private AutomationElement[] LocateRead(AutomationElement root, AutomationProfile profile, string key)
        {
            var field = profile.Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase) && UiaAutomationService.HasSelector(f));
            return field == null ? null : automation.LocateParts(root, field);
        }

        private void Publish(PatientContext next)
        {
            PatientContext previous;
            lock (sync)
            {
                previous = current;
                var same = (previous == null && next == null) ||
                    (previous != null && next != null && previous.Key == next.Key &&
                     previous.PatientName == next.PatientName && previous.BirthYear == next.BirthYear);
                if (same) return;
                current = next;
            }
            var handler = ContextChanged;
            if (handler != null) handler(this, new PatientContextEventArgs(next));
        }

        private static bool IsShown(AutomationElement element)
        {
            try { return !element.Current.IsOffscreen; }
            catch (ElementNotAvailableException) { return false; }
        }

        private static bool TitleMatches(AutomationProfile profile, string title)
        {
            var pattern = profile.WindowTitleRegex;
            if (string.IsNullOrWhiteSpace(pattern) || pattern == ".*") return true;
            try { return Regex.IsMatch(title ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
            catch (ArgumentException) { return true; }
        }

        private static IEnumerable<string> SplitNames(string value)
        {
            return (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0);
        }

        public static bool IsPatientIdField(FieldMapping field)
        {
            return string.Equals(field.Key, "PatientId", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(field.Operation, "Verify", StringComparison.OrdinalIgnoreCase) &&
                UiaAutomationService.HasSelector(field);
        }

        private static AutomationProfile CloneWithoutValues(AutomationProfile profile)
        {
            return new AutomationProfile
            {
                Name = profile.Name,
                WindowTitleRegex = profile.WindowTitleRegex,
                ProcessNames = profile.ProcessNames,
                StopBeforeSave = profile.StopBeforeSave,
                CloudFormCode = profile.CloudFormCode,
                AllowSaveAfterFill = profile.AllowSaveAfterFill,
                SaveAction = profile.SaveAction == null ? new UiActionMapping() : profile.SaveAction.Clone(),
                CloseAction = profile.CloseAction == null ? new UiActionMapping() : profile.CloseAction.Clone(),
                Fields = profile.Fields.Select(f => f.CloneWithoutValue()).ToList()
            };
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
