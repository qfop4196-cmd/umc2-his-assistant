using System;
using System.Collections.Generic;

namespace Umc2.IntakeServer
{
    internal static class IntakeStatus
    {
        public const string Pending = "pending";      // BN đã gửi, chờ điều dưỡng duyệt
        public const string Approved = "approved";    // Điều dưỡng đã duyệt, chờ bác sĩ điền vào HIS
        public const string Claimed = "claimed";      // Máy bác sĩ đang xử lý
        public const string Completed = "completed";  // Bác sĩ xác nhận đã điền & lưu trên HIS
        public const string Rejected = "rejected";    // Điều dưỡng từ chối (trùng, spam, sai người...)

        public static readonly string[] All = { Pending, Approved, Claimed, Completed, Rejected };
    }

    /// <summary>One patient self-reported intake. Persisted encrypted (DPAPI); never logged.</summary>
    public sealed class IntakeRecord
    {
        public IntakeRecord()
        {
            Id = string.Empty;
            Code = string.Empty;
            Status = IntakeStatus.Pending;
            Source = string.Empty;
            FormVersion = string.Empty;
            CreatedAt = string.Empty;
            UpdatedAt = string.Empty;
            Patient = new PatientInfo();
            Answers = new Dictionary<string, object>();
            Review = new ReviewInfo();
            Agent = new AgentInfo();
            History = new List<HistoryEntry>();
        }

        public string Id { get; set; }
        public string Code { get; set; }
        public string Status { get; set; }
        public string Source { get; set; }
        public string FormVersion { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public PatientInfo Patient { get; set; }
        public Dictionary<string, object> Answers { get; set; }
        public ReviewInfo Review { get; set; }
        public AgentInfo Agent { get; set; }
        public List<HistoryEntry> History { get; set; }
    }

    public sealed class PatientInfo
    {
        public PatientInfo()
        {
            FullName = BirthDate = Gender = Phone = NationalId = HisPatientId = FilledBy = Relation = string.Empty;
        }

        public string FullName { get; set; }
        public string BirthDate { get; set; }
        public int BirthYear { get; set; }
        public string Gender { get; set; }
        public string Phone { get; set; }
        public string NationalId { get; set; }
        public string HisPatientId { get; set; }
        public string FilledBy { get; set; }
        public string Relation { get; set; }
    }

    public sealed class ReviewInfo
    {
        public ReviewInfo()
        {
            HisPatientId = Note = ReviewedBy = ReviewedAt = ApprovedBy = ApprovedAt = RejectedBy = RejectedAt = RejectReason = string.Empty;
            Fields = new Dictionary<string, string>();
        }

        public string HisPatientId { get; set; }
        public Dictionary<string, string> Fields { get; set; }
        public string Note { get; set; }
        public string ReviewedBy { get; set; }
        public string ReviewedAt { get; set; }
        public string ApprovedBy { get; set; }
        public string ApprovedAt { get; set; }
        public string RejectedBy { get; set; }
        public string RejectedAt { get; set; }
        public string RejectReason { get; set; }
    }

    public sealed class AgentInfo
    {
        public AgentInfo()
        {
            DeviceId = DeviceName = ClaimedAt = CompletedAt = Result = Summary = ObservedHisPatientId = string.Empty;
        }

        public string DeviceId { get; set; }
        public string DeviceName { get; set; }
        public string ClaimedAt { get; set; }
        public string CompletedAt { get; set; }
        public string Result { get; set; }
        public string Summary { get; set; }
        public string ObservedHisPatientId { get; set; }
        public int FilledCount { get; set; }
    }

    public sealed class HistoryEntry
    {
        public string At { get; set; }
        public string Actor { get; set; }
        public string Action { get; set; }
        public string Detail { get; set; }
    }

    public sealed class StaffUser
    {
        public StaffUser()
        {
            Username = DisplayName = Role = Salt = Hash = CreatedAt = string.Empty;
            Iterations = Passwords.DefaultIterations;
        }

        public string Username { get; set; }
        public string DisplayName { get; set; }
        public string Role { get; set; }
        public string Salt { get; set; }
        public string Hash { get; set; }
        public int Iterations { get; set; }
        public bool Disabled { get; set; }
        public bool MustChangePassword { get; set; }
        public string CreatedAt { get; set; }
    }

    public sealed class AgentDevice
    {
        public AgentDevice()
        {
            Id = Name = TokenHash = CreatedAt = CreatedBy = LastSeenAt = LastIp = MachineName = string.Empty;
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string MachineName { get; set; }
        public string TokenHash { get; set; }
        public string CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public string LastSeenAt { get; set; }
        public string LastIp { get; set; }
        public bool Revoked { get; set; }
    }

    public static class Roles
    {
        public const string Admin = "admin";
        public const string Nurse = "nurse";
    }
}
