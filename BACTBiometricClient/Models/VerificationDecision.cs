using System;

namespace BACTBiometricClient.Models
{
    /// <summary>
    /// Represents a verification decision with manual override capability
    /// </summary>
    public class VerificationDecision
    {
        public string FinalDecision { get; set; } = "";
        public bool IsManualOverride { get; set; } = false;
        public string OverrideReason { get; set; } = "";
        public string Notes { get; set; } = "";
        public DateTime DecisionTime { get; set; } = DateTime.Now;
        public string DecisionMaker { get; set; } = "";
    }
}