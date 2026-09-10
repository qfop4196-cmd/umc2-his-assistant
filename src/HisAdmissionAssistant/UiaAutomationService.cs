using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace HisAdmissionAssistant
{
    public sealed class UiaAutomationService
    {
        private static readonly string[] DangerousTokens =
        {
            "butluu", "btnluu", "save", "xacnhan", "xácnhận", "xác nhận",
            "dongy", "đồngý", "đồng ý", "butok", "btnok"
        };

        private const uint WmSetText = 0x000C;
        private const uint SmtoAbortIfHung = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            string lParam,
            uint flags,
            uint timeout,
            out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

        private const uint GaRoot = 2;

        public IList<TargetWindow> FindWindows(AutomationProfile profile)
        {
            var result = new List<TargetWindow>();
            var processNames = ParseProcessNames(profile.ProcessNames);
            Regex titleRegex;
            try
            {
                titleRegex = new Regex(
                    string.IsNullOrWhiteSpace(profile.WindowTitleRegex) ? ".*" : profile.WindowTitleRegex,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException("Regex tiêu đề cửa sổ không hợp lệ: " + ex.Message, ex);
            }

            AutomationElementCollection windows = AutomationElement.RootElement.FindAll(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

            foreach (AutomationElement window in windows)
            {
                try
                {
                    var pid = window.Current.ProcessId;
                    var title = window.Current.Name ?? string.Empty;
                    string processName;
                    try { processName = Process.GetProcessById(pid).ProcessName; }
                    catch { processName = "PID-" + pid; }

                    if (processNames.Count > 0 && !processNames.Contains(processName)) continue;
                    if (!titleRegex.IsMatch(title)) continue;
                    result.Add(new TargetWindow
                    {
                        Element = window,
                        ProcessId = pid,
                        ProcessName = processName,
                        Title = title
                    });
                }
                catch (ElementNotAvailableException)
                {
                }
            }
            return result.OrderBy(w => w.ProcessName).ThenBy(w => w.Title).ToList();
        }

        public IList<ControlSnapshot> Scan(AutomationElement window, int limit)
        {
            if (window == null) throw new ArgumentNullException("window");
            var snapshots = new List<ControlSnapshot>();
            AutomationElementCollection elements = window.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            var count = Math.Min(elements.Count, Math.Max(1, limit));
            for (var i = 0; i < count; i++)
            {
                try
                {
                    var current = elements[i].Current;
                    if (string.IsNullOrWhiteSpace(current.AutomationId) && string.IsNullOrWhiteSpace(current.Name)) continue;
                    snapshots.Add(new ControlSnapshot
                    {
                        AutomationId = current.AutomationId ?? string.Empty,
                        Name = current.Name ?? string.Empty,
                        ControlType = CleanControlType(current.ControlType),
                        ClassName = current.ClassName ?? string.Empty,
                        IsEnabled = current.IsEnabled,
                        Bounds = FormatBounds(current.BoundingRectangle)
                    });
                }
                catch (ElementNotAvailableException)
                {
                }
            }
            return snapshots;
        }

        public ControlSnapshot CaptureAt(int x, int y, out AutomationElement capturedElement)
        {
            var element = AutomationElement.FromPoint(new Point(x, y));
            element = NormalizeInteractiveElement(element);
            capturedElement = element;
            var current = element.Current;
            return new ControlSnapshot
            {
                AutomationId = current.AutomationId ?? string.Empty,
                Name = current.Name ?? string.Empty,
                ControlType = CleanControlType(current.ControlType),
                ClassName = current.ClassName ?? string.Empty,
                IsEnabled = current.IsEnabled,
                Bounds = FormatBounds(current.BoundingRectangle)
            };
        }

        public IList<FieldResult> ValidateMappings(AutomationElement window, IEnumerable<FieldMapping> fields)
        {
            var results = new List<FieldResult>();
            foreach (var field in fields.Where(f => f.EnabledForFill))
            {
                try
                {
                    if (IsDangerous(field))
                    {
                        results.Add(Result(field, false, false, "Bị chặn vì selector giống nút Lưu/Xác nhận"));
                        continue;
                    }
                    if (!HasSelector(field))
                    {
                        results.Add(Result(field, false, false, "Chưa gán selector (AutomationId/Name)"));
                        continue;
                    }
                    var element = FindField(window, field);
                    results.Add(element == null
                        ? Result(field, false, false, "Không tìm thấy")
                        : Result(field, true, false, element.Current.IsEnabled ? "Đã tìm thấy" : "Đã tìm thấy nhưng đang bị khóa"));
                }
                catch (Exception ex)
                {
                    results.Add(Result(field, false, false, "Lỗi: " + ex.Message));
                }
            }
            return results;
        }

        public IList<FieldResult> Apply(AutomationElement window, IEnumerable<FieldMapping> fields)
        {
            if (window == null) throw new ArgumentNullException("window");
            var selected = fields.Where(f => f.EnabledForFill && !IsReadOnly(f)).ToList();
            var resolved = new Dictionary<FieldMapping, AutomationElement>();
            var results = new List<FieldResult>();
            var preflightFailed = false;

            foreach (var field in selected)
            {
                if (IsDangerous(field))
                {
                    results.Add(Result(field, false, false, "Bị chặn vì selector giống nút Lưu/Xác nhận"));
                    preflightFailed = true;
                    continue;
                }
                if (field.Required && string.IsNullOrWhiteSpace(field.Value))
                {
                    results.Add(Result(field, false, false, "Thiếu dữ liệu bắt buộc"));
                    preflightFailed = true;
                    continue;
                }
                if (string.IsNullOrWhiteSpace(field.Value) && !IsVerify(field))
                {
                    results.Add(Result(field, true, false, "Bỏ qua vì chưa nhập dữ liệu"));
                    continue;
                }

                AutomationElement element;
                try { element = FindField(window, field); }
                catch (Exception ex)
                {
                    results.Add(Result(field, false, false, "Lỗi selector: " + ex.Message));
                    preflightFailed = true;
                    continue;
                }
                if (element == null)
                {
                    results.Add(Result(field, false, false, "Không tìm thấy control"));
                    if (field.Required || !string.IsNullOrWhiteSpace(field.Value)) preflightFailed = true;
                    continue;
                }
                if (!element.Current.IsEnabled)
                {
                    results.Add(Result(field, true, false, "Control đang bị khóa"));
                    preflightFailed = true;
                    continue;
                }
                resolved[field] = element;

                if (IsVerify(field))
                {
                    string actual;
                    if (!TryReadValue(element, out actual))
                    {
                        results.Add(Result(field, true, false, "Không đọc được để đối chiếu"));
                        preflightFailed = true;
                    }
                    else if (!EqualNormalized(actual, field.Value))
                    {
                        results.Add(Result(field, true, false, "Không khớp dữ liệu đang mở — đã hủy toàn bộ"));
                        preflightFailed = true;
                    }
                    else
                    {
                        results.Add(Result(field, true, false, "Đối chiếu khớp"));
                    }
                }
            }

            if (preflightFailed)
            {
                foreach (var field in selected.Where(f => resolved.ContainsKey(f) && !IsVerify(f)))
                    if (!results.Any(r => object.ReferenceEquals(r.Field, field)))
                        results.Add(Result(field, true, false, "Chưa điền vì bước kiểm tra trước thất bại"));
                return OrderResults(selected, results);
            }

            foreach (var field in selected.Where(f => !IsVerify(f) && resolved.ContainsKey(f) && !string.IsNullOrWhiteSpace(f.Value)))
            {
                try
                {
                    SetElementValue(resolved[field], PrepareText(field));
                    results.Add(Result(field, true, true, "Đã điền"));
                }
                catch (Exception ex)
                {
                    results.Add(Result(field, true, false, "Không điền được: " + ex.Message));
                }
            }
            return OrderResults(selected, results);
        }

        public void InvokeAuthorizedAction(AutomationElement window, UiActionMapping action, string actionLabel)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (action == null || (string.IsNullOrWhiteSpace(action.AutomationId) && string.IsNullOrWhiteSpace(action.Name)))
                throw new InvalidOperationException("Chưa cấu hình selector cho thao tác " + actionLabel + ".");

            var mapping = new FieldMapping
            {
                AutomationId = action.AutomationId,
                Name = action.Name,
                ClassName = action.ClassName,
                MatchIndex = action.MatchIndex,
                ControlType = "Button"
            };
            var element = FindField(window, mapping);
            if (element == null) throw new InvalidOperationException("Không tìm thấy nút " + actionLabel + ".");
            if (!element.Current.IsEnabled) throw new InvalidOperationException("Nút " + actionLabel + " đang bị khóa.");
            object pattern;
            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                throw new InvalidOperationException("Nút " + actionLabel + " không hỗ trợ UIA InvokePattern.");
            ((InvokePattern)pattern).Invoke();
        }

        /// <summary>Finds the control for a field (null when not found or the field has no selector).</summary>
        public AutomationElement Locate(AutomationElement window, FieldMapping field)
        {
            if (window == null || field == null || !HasSelector(field)) return null;
            return FindField(window, field);
        }

        /// <summary>Reads the current text of a control without changing it.</summary>
        public bool TryRead(AutomationElement element, out string value)
        {
            value = string.Empty;
            if (element == null) return false;
            try { return TryReadValue(element, out value); }
            catch (ElementNotAvailableException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary>
        /// Current non-empty contents of the fields that would be written, so the caller can avoid overwriting
        /// text a doctor already typed on HIS.
        /// </summary>
        public IDictionary<FieldMapping, string> ReadExistingValues(AutomationElement window, IEnumerable<FieldMapping> fields)
        {
            var result = new Dictionary<FieldMapping, string>();
            foreach (var field in fields.Where(f => f.EnabledForFill && !IsVerify(f) && !IsReadOnly(f) && !string.IsNullOrWhiteSpace(f.Value)))
            {
                try
                {
                    var element = Locate(window, field);
                    string current;
                    if (element != null && TryRead(element, out current) && !string.IsNullOrWhiteSpace(current))
                        result[field] = current;
                }
                catch (ElementNotAvailableException)
                {
                }
            }
            return result;
        }

        /// <summary>Top-level window currently in the foreground, if it belongs to one of <paramref name="processNames"/>.</summary>
        public TargetWindow ForegroundWindow(ICollection<string> processNames)
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            var root = GetAncestor(hwnd, GaRoot);
            if (root != IntPtr.Zero) hwnd = root;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            if (pid == 0 || pid == (uint)Process.GetCurrentProcess().Id) return null;
            string processName;
            try { processName = Process.GetProcessById((int)pid).ProcessName; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            if (processNames != null && processNames.Count > 0 && !processNames.Contains(processName, StringComparer.OrdinalIgnoreCase)) return null;
            try
            {
                var element = AutomationElement.FromHandle(hwnd);
                if (element == null) return null;
                return new TargetWindow { Element = element, ProcessId = (int)pid, ProcessName = processName, Title = element.Current.Name ?? string.Empty };
            }
            catch (ElementNotAvailableException) { return null; }
            catch (ArgumentException) { return null; }
        }

        /// <summary>True if the foreground window belongs to this assistant (doctor clicked the assistant).</summary>
        public static bool AssistantIsForeground()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            return pid == (uint)Process.GetCurrentProcess().Id;
        }

        public static bool HasSelector(FieldMapping field)
        {
            return field != null && (!string.IsNullOrWhiteSpace(field.AutomationId) || !string.IsNullOrWhiteSpace(field.Name));
        }

        public static bool IsReadOnly(FieldMapping field)
        {
            return string.Equals(field.Operation, "Read", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>WinForms multi-line TextBoxes need CRLF; single-line boxes get the text flattened.</summary>
        public static string PrepareText(FieldMapping field)
        {
            var value = (field.Value ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (field.Multiline) return value.Replace("\n", "\r\n");
            return Regex.Replace(value, @"\s*\n+\s*", "; ").Trim();
        }

        private static IList<FieldResult> OrderResults(IList<FieldMapping> selected, IList<FieldResult> results)
        {
            return selected.Select(field => results.FirstOrDefault(r => object.ReferenceEquals(r.Field, field))
                ?? Result(field, false, false, "Không xử lý")).ToList();
        }

        private static FieldResult Result(FieldMapping field, bool found, bool changed, string message)
        {
            return new FieldResult { Field = field, Found = found, Changed = changed, Message = message };
        }

        private static AutomationElement FindField(AutomationElement window, FieldMapping field)
        {
            var conditions = new List<Condition>();
            var controlType = ParseControlType(field.ControlType);
            if (controlType != null)
                conditions.Add(new PropertyCondition(AutomationElement.ControlTypeProperty, controlType));
            if (!string.IsNullOrWhiteSpace(field.ClassName))
                conditions.Add(new PropertyCondition(AutomationElement.ClassNameProperty, field.ClassName, PropertyConditionFlags.IgnoreCase));

            AutomationElement found = null;
            if (!string.IsNullOrWhiteSpace(field.AutomationId))
            {
                var byId = new List<Condition>(conditions);
                byId.Add(new PropertyCondition(AutomationElement.AutomationIdProperty, field.AutomationId, PropertyConditionFlags.IgnoreCase));
                found = FindByIndex(window, Combine(byId), field.MatchIndex);
                if (found == null)
                    found = FindByIndex(
                        window,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, field.AutomationId, PropertyConditionFlags.IgnoreCase),
                        field.MatchIndex);
            }
            if (found == null && !string.IsNullOrWhiteSpace(field.Name))
            {
                var byName = new List<Condition>(conditions);
                byName.Add(new PropertyCondition(AutomationElement.NameProperty, field.Name, PropertyConditionFlags.IgnoreCase));
                found = FindByIndex(window, Combine(byName), field.MatchIndex);
                if (found == null)
                    found = FindByIndex(
                        window,
                        new PropertyCondition(AutomationElement.NameProperty, field.Name, PropertyConditionFlags.IgnoreCase),
                        field.MatchIndex);
            }
            // No AutomationId and no Name: refuse to guess (the old "first Edit on the form" fallback could write into the wrong box).
            return found;
        }

        private static Condition Combine(IList<Condition> conditions)
        {
            if (conditions.Count == 0) return Condition.TrueCondition;
            if (conditions.Count == 1) return conditions[0];
            return new AndCondition(conditions.ToArray());
        }

        private static AutomationElement FindByIndex(AutomationElement root, Condition condition, int index)
        {
            var all = root.FindAll(TreeScope.Descendants, condition);
            return all.Count > Math.Max(0, index) ? all[Math.Max(0, index)] : null;
        }

        private static void SetElementValue(AutomationElement element, string value)
        {
            try { element.SetFocus(); }
            catch (InvalidOperationException) { }
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                var valuePattern = (ValuePattern)pattern;
                if (valuePattern.Current.IsReadOnly)
                    throw new InvalidOperationException("Control được UIA đánh dấu chỉ đọc.");
                valuePattern.SetValue(value);
                return;
            }
            if (TrySetNativeText(element, value)) return;
            if (TrySelectItem(element, value)) return;
            throw new InvalidOperationException("Control không hỗ trợ UIA ValuePattern hoặc chọn danh sách.");
        }

        private static bool TrySelectItem(AutomationElement element, string value)
        {
            var type = element.Current.ControlType;
            if (type != ControlType.ComboBox && type != ControlType.List && type != ControlType.ListItem)
                return false;
            object expandPatternObject;
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out expandPatternObject))
            {
                var expandPattern = (ExpandCollapsePattern)expandPatternObject;
                try { expandPattern.Expand(); }
                catch (InvalidOperationException) { }
                Thread.Sleep(120);
            }

            var nameCondition = new PropertyCondition(
                AutomationElement.NameProperty,
                value,
                PropertyConditionFlags.IgnoreCase);
            var item = element.FindFirst(TreeScope.Descendants, nameCondition);
            if (item == null)
            {
                var pid = element.Current.ProcessId;
                var sameProcess = new PropertyCondition(AutomationElement.ProcessIdProperty, pid);
                var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, sameProcess);
                foreach (AutomationElement processRoot in roots)
                {
                    item = processRoot.FindFirst(TreeScope.Descendants, nameCondition);
                    if (item != null) break;
                }
            }
            if (item == null) return false;

            object pattern;
            if (item.TryGetCurrentPattern(SelectionItemPattern.Pattern, out pattern))
                ((SelectionItemPattern)pattern).Select();
            else if (item.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                ((InvokePattern)pattern).Invoke();
            else
                return false;

            if (expandPatternObject != null)
            {
                try { ((ExpandCollapsePattern)expandPatternObject).Collapse(); }
                catch (InvalidOperationException) { }
            }
            return true;
        }

        private static bool TryReadValue(AutomationElement element, out string value)
        {
            object pattern;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out pattern))
            {
                value = ((ValuePattern)pattern).Current.Value ?? string.Empty;
                return true;
            }
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern))
            {
                value = ((TextPattern)pattern).DocumentRange.GetText(-1).TrimEnd('\r', '\n');
                return true;
            }
            var handle = element.Current.NativeWindowHandle;
            if (handle != 0)
            {
                var length = Math.Max(0, GetWindowTextLength(new IntPtr(handle)));
                var buffer = new StringBuilder(length + 2);
                GetWindowText(new IntPtr(handle), buffer, buffer.Capacity);
                value = buffer.ToString();
                return true;
            }
            value = string.Empty;
            return false;
        }

        private static bool TrySetNativeText(AutomationElement element, string value)
        {
            var type = element.Current.ControlType;
            if (type != ControlType.Edit && type != ControlType.Document) return false;
            var handle = element.Current.NativeWindowHandle;
            if (handle == 0) return false;
            IntPtr result;
            return SendMessageTimeout(
                new IntPtr(handle),
                WmSetText,
                IntPtr.Zero,
                value,
                SmtoAbortIfHung,
                2000,
                out result) != IntPtr.Zero;
        }

        private static bool EqualNormalized(string left, string right)
        {
            return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value)
        {
            return Regex.Replace(value ?? string.Empty, "[^0-9A-Za-zÀ-ỹ]", string.Empty);
        }

        private static AutomationElement NormalizeInteractiveElement(AutomationElement element)
        {
            var walker = TreeWalker.ControlViewWalker;
            for (var i = 0; i < 6 && element != null; i++)
            {
                try
                {
                    object ignored;
                    var type = element.Current.ControlType;
                    if (type == ControlType.Edit || type == ControlType.Document || type == ControlType.ComboBox || type == ControlType.List ||
                        type == ControlType.ListItem || type == ControlType.CheckBox || type == ControlType.Button ||
                        element.TryGetCurrentPattern(ValuePattern.Pattern, out ignored))
                        return element;
                    element = walker.GetParent(element);
                }
                catch (ElementNotAvailableException)
                {
                    break;
                }
            }
            if (element == null) throw new InvalidOperationException("Không lấy được control tại vị trí con trỏ.");
            return element;
        }

        private static HashSet<string> ParseProcessNames(string value)
        {
            return new HashSet<string>(
                (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        }

        private static ControlType ParseControlType(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "edit": return ControlType.Edit;
                case "document": return ControlType.Document;
                case "combobox": return ControlType.ComboBox;
                case "list": return ControlType.List;
                case "listitem": return ControlType.ListItem;
                case "checkbox": return ControlType.CheckBox;
                case "radiobutton": return ControlType.RadioButton;
                case "text": return ControlType.Text;
                case "button": return ControlType.Button;
                case "spinner": return ControlType.Spinner;
                case "custom": return ControlType.Custom;
                default: return null;
            }
        }

        private static string CleanControlType(ControlType type)
        {
            if (type == null) return string.Empty;
            return type.ProgrammaticName.Replace("ControlType.", string.Empty);
        }

        private static string FormatBounds(Rect rect)
        {
            return string.Format("{0:0},{1:0} {2:0}x{3:0}", rect.X, rect.Y, rect.Width, rect.Height);
        }

        private static bool IsVerify(FieldMapping field)
        {
            return string.Equals(field.Operation, "Verify", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDangerous(FieldMapping field)
        {
            var haystack = string.Join(" ", new[] { field.Key, field.Label, field.AutomationId, field.Name })
                .Replace(" ", string.Empty).ToLowerInvariant();
            return DangerousTokens.Any(token => haystack.Contains(token.Replace(" ", string.Empty).ToLowerInvariant()));
        }
    }
}
