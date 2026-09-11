using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Umc2.IntakeServer
{
    /// <summary>Explicit camelCase projections: only what each audience needs leaves the server.</summary>
    internal static class Views
    {
        public static Dictionary<string, object> StaffSummary(IntakeRecord r)
        {
            return new Dictionary<string, object>
            {
                { "id", r.Id },
                { "code", r.Code },
                { "status", r.Status },
                { "source", r.Source },
                { "createdAt", r.CreatedAt },
                { "updatedAt", r.UpdatedAt },
                { "fullName", r.Patient.FullName },
                { "birthYear", r.Patient.BirthYear },
                { "gender", r.Patient.Gender },
                { "phoneMasked", TextUtil.MaskPhone(r.Patient.Phone) },
                { "chiefComplaint", TextUtil.FirstLine(AnswerText(r, "chiefComplaint"), 90) },
                { "hisPatientId", r.Review.HisPatientId },
                { "selfHisPatientId", r.Patient.HisPatientId },
                { "approvedBy", r.Review.ApprovedBy },
                { "approvedAt", r.Review.ApprovedAt },
                { "deviceName", r.Agent.DeviceName },
                { "claimedAt", r.Agent.ClaimedAt },
                { "completedAt", r.Agent.CompletedAt },
                { "agentResult", r.Agent.Result }
            };
        }

        public static Dictionary<string, object> StaffDetail(IntakeRecord r)
        {
            var detail = StaffSummary(r);
            detail["formVersion"] = r.FormVersion;
            detail["patient"] = new Dictionary<string, object>
            {
                { "fullName", r.Patient.FullName },
                { "birthDate", r.Patient.BirthDate },
                { "birthYear", r.Patient.BirthYear },
                { "gender", r.Patient.Gender },
                { "phone", r.Patient.Phone },
                { "nationalId", r.Patient.NationalId },
                { "hisPatientId", r.Patient.HisPatientId },
                { "filledBy", r.Patient.FilledBy },
                { "relation", r.Patient.Relation }
            };
            detail["answers"] = r.Answers;
            detail["review"] = new Dictionary<string, object>
            {
                { "hisPatientId", r.Review.HisPatientId },
                { "fields", r.Review.Fields },
                { "note", r.Review.Note },
                { "reviewedBy", r.Review.ReviewedBy },
                { "reviewedAt", r.Review.ReviewedAt },
                { "approvedBy", r.Review.ApprovedBy },
                { "approvedAt", r.Review.ApprovedAt },
                { "rejectedBy", r.Review.RejectedBy },
                { "rejectedAt", r.Review.RejectedAt },
                { "rejectReason", r.Review.RejectReason }
            };
            detail["agent"] = new Dictionary<string, object>
            {
                { "deviceName", r.Agent.DeviceName },
                { "claimedAt", r.Agent.ClaimedAt },
                { "completedAt", r.Agent.CompletedAt },
                { "result", r.Agent.Result },
                { "summary", r.Agent.Summary },
                { "observedHisPatientId", r.Agent.ObservedHisPatientId },
                { "filledCount", r.Agent.FilledCount }
            };
            detail["history"] = r.History.Select(h => new Dictionary<string, object>
            {
                { "at", h.At }, { "actor", h.Actor }, { "action", h.Action }, { "detail", h.Detail }
            }).ToList();
            return detail;
        }

        public static Dictionary<string, object> AgentSummary(IntakeRecord r)
        {
            return new Dictionary<string, object>
            {
                { "id", r.Id },
                { "code", r.Code },
                { "status", r.Status },
                { "fullName", r.Patient.FullName },
                { "birthYear", r.Patient.BirthYear },
                { "gender", r.Patient.Gender },
                { "hisPatientId", r.Review.HisPatientId },
                { "chiefComplaint", TextUtil.FirstLine(AnswerText(r, "chiefComplaint"), 90) },
                { "createdAt", r.CreatedAt },
                { "approvedAt", r.Review.ApprovedAt },
                { "approvedBy", r.Review.ApprovedBy },
                { "claimedBy", r.Status == IntakeStatus.Claimed ? r.Agent.DeviceName : string.Empty },
                { "claimedById", r.Status == IntakeStatus.Claimed ? r.Agent.DeviceId : string.Empty }
            };
        }

        public static Dictionary<string, object> AgentDetail(IntakeRecord r)
        {
            var detail = AgentSummary(r);
            detail["fields"] = AgentFields(r);
            return detail;
        }

        /// <summary>Flat field map keyed like the assistant's profile Field Keys. PatientId only when a nurse confirmed it.</summary>
        public static Dictionary<string, string> AgentFields(IntakeRecord r)
        {
            var fields = new Dictionary<string, string>(r.Review.Fields ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
            fields.Remove("PatientId");
            if (!string.IsNullOrEmpty(r.Review.HisPatientId)) fields["PatientId"] = r.Review.HisPatientId;
            return fields;
        }

        public static Dictionary<string, object> User(StaffUser u)
        {
            return new Dictionary<string, object>
            {
                { "username", u.Username },
                { "displayName", u.DisplayName },
                { "role", u.Role },
                { "disabled", u.Disabled },
                { "mustChangePassword", u.MustChangePassword },
                { "createdAt", u.CreatedAt }
            };
        }

        public static Dictionary<string, object> Device(AgentDevice d)
        {
            return new Dictionary<string, object>
            {
                { "id", d.Id },
                { "name", d.Name },
                { "machineName", d.MachineName },
                { "createdAt", d.CreatedAt },
                { "createdBy", d.CreatedBy },
                { "lastSeenAt", d.LastSeenAt },
                { "lastIp", d.LastIp },
                { "revoked", d.Revoked }
            };
        }

        internal static string AnswerText(IntakeRecord r, string key)
        {
            object value;
            if (r.Answers == null || !r.Answers.TryGetValue(key, out value) || value == null) return string.Empty;
            var text = value as string;
            if (text != null) return text;
            var list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                var parts = new List<string>();
                foreach (var item in list) if (item != null) parts.Add(Convert.ToString(item, CultureInfo.InvariantCulture));
                return string.Join(", ", parts.ToArray());
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
