using System;
using System.Collections.Generic;
using System.Windows.Automation;
using System.Xml.Serialization;

namespace HisAdmissionAssistant
{
    [Serializable]
    [XmlRoot("AutomationProfile")]
    public sealed class AutomationProfile
    {
        [XmlAttribute]
        public string Name { get; set; }

        [XmlAttribute]
        public string WindowTitleRegex { get; set; }

        [XmlAttribute]
        public string ProcessNames { get; set; }

        [XmlAttribute]
        public bool StopBeforeSave { get; set; }

        [XmlAttribute]
        public string CloudFormCode { get; set; }

        [XmlAttribute]
        public bool AllowSaveAfterFill { get; set; }

        [XmlElement]
        public UiActionMapping SaveAction { get; set; }

        [XmlElement]
        public UiActionMapping CloseAction { get; set; }

        [XmlArray("Fields")]
        [XmlArrayItem("Field")]
        public List<FieldMapping> Fields { get; set; }

        public AutomationProfile()
        {
            Name = string.Empty;
            WindowTitleRegex = ".*HIS.*";
            ProcessNames = "UMC2HIS,MQHIS,MQEMR";
            StopBeforeSave = true;
            CloudFormCode = string.Empty;
            AllowSaveAfterFill = false;
            SaveAction = new UiActionMapping();
            CloseAction = new UiActionMapping();
            Fields = new List<FieldMapping>();
        }
    }

    [Serializable]
    public sealed class UiActionMapping
    {
        [XmlAttribute]
        public string AutomationId { get; set; }

        [XmlAttribute]
        public string Name { get; set; }

        [XmlAttribute]
        public string ClassName { get; set; }

        [XmlAttribute]
        public int MatchIndex { get; set; }

        public UiActionMapping()
        {
            AutomationId = string.Empty;
            Name = string.Empty;
            ClassName = string.Empty;
        }

        public UiActionMapping Clone()
        {
            return new UiActionMapping
            {
                AutomationId = AutomationId,
                Name = Name,
                ClassName = ClassName,
                MatchIndex = MatchIndex
            };
        }
    }

    public sealed class CloudAutomationJob
    {
        public string Id { get; set; }
        public string AdmissionId { get; set; }
        public string FormCode { get; set; }
        public string FormName { get; set; }
        public string SignaturePolicy { get; set; }
        public bool IsOffline { get; set; }
        public IDictionary<string, string> Fields { get; set; }

        public CloudAutomationJob()
        {
            Id = string.Empty;
            AdmissionId = string.Empty;
            FormCode = string.Empty;
            FormName = string.Empty;
            SignaturePolicy = string.Empty;
            IsOffline = false;
            Fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    [Serializable]
    public sealed class FieldMapping
    {
        [XmlAttribute]
        public string Key { get; set; }

        [XmlAttribute]
        public string Label { get; set; }

        [XmlAttribute]
        public string AutomationId { get; set; }

        [XmlAttribute]
        public string Name { get; set; }

        [XmlAttribute]
        public string ControlType { get; set; }

        [XmlAttribute]
        public string ClassName { get; set; }

        [XmlAttribute]
        public int MatchIndex { get; set; }

        [XmlAttribute]
        public bool Required { get; set; }

        [XmlAttribute]
        public bool Multiline { get; set; }

        [XmlAttribute]
        public string Operation { get; set; }

        [XmlIgnore]
        public string Value { get; set; }

        [XmlIgnore]
        public bool EnabledForFill { get; set; }

        [XmlIgnore]
        public string Status { get; set; }

        public FieldMapping()
        {
            Key = string.Empty;
            Label = string.Empty;
            AutomationId = string.Empty;
            Name = string.Empty;
            ControlType = "Edit";
            ClassName = string.Empty;
            MatchIndex = 0;
            Operation = "Set";
            Value = string.Empty;
            EnabledForFill = true;
            Status = "Chưa kiểm tra";
        }

        public FieldMapping CloneWithoutValue()
        {
            return new FieldMapping
            {
                Key = Key,
                Label = Label,
                AutomationId = AutomationId,
                Name = Name,
                ControlType = ControlType,
                ClassName = ClassName,
                MatchIndex = MatchIndex,
                Required = Required,
                Multiline = Multiline,
                Operation = Operation
            };
        }
    }

    public sealed class TargetWindow
    {
        public AutomationElement Element { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; }
        public string Title { get; set; }

        public override string ToString()
        {
            return string.Format("{0} — {1} (PID {2})", ProcessName, Title, ProcessId);
        }
    }

    public sealed class ControlSnapshot
    {
        public string AutomationId { get; set; }
        public string Name { get; set; }
        public string ControlType { get; set; }
        public string ClassName { get; set; }
        public bool IsEnabled { get; set; }
        public string Bounds { get; set; }
    }

    public sealed class FieldResult
    {
        public FieldMapping Field { get; set; }
        public bool Found { get; set; }
        public bool Changed { get; set; }
        public string Message { get; set; }
    }
}
