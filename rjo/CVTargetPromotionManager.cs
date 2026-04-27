// 🔥 CVTargetPromotionManager.cs
// Primary/secondary promotion logic.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Vision.Targeting
{
    public enum TargetRole
    {
        None,
        Primary,
        Secondary
    }

    public class CVTarget
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public float Score { get; set; }
        public bool IsVisible { get; set; } = true;
        public TargetRole Role { get; set; } = TargetRole.None;
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }

    public class CVTargetPromotionManager
    {
        public TimeSpan PrimaryStickiness { get; set; } = TimeSpan.FromSeconds(1.0);
        public float PrimaryScoreThreshold { get; set; } = 0.7f;
        public float SecondaryScoreThreshold { get; set; } = 0.4f;
        public int MaxSecondaryTargets { get; set; } = 2;

        private string? _currentPrimaryId;
        private DateTime _primaryLockedUntilUtc;

        public void UpdateRoles(IList<CVTarget> targets, DateTime nowUtc)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));

            // Clear roles
            foreach (var t in targets)
                t.Role = TargetRole.None;

            // Maintain primary if still valid and within stickiness window
            CVTarget? primary = null;
            if (_currentPrimaryId != null && nowUtc <= _primaryLockedUntilUtc)
            {
                primary = targets.FirstOrDefault(t =>
                    t.Id == _currentPrimaryId &&
                    t.IsVisible &&
                    t.Score >= PrimaryScoreThreshold);
            }

            // If no valid primary, pick best candidate
            if (primary == null)
            {
                primary = targets
                    .Where(t => t.IsVisible && t.Score >= PrimaryScoreThreshold)
                    .OrderByDescending(t => t.Score)
                    .FirstOrDefault();

                if (primary != null)
                {
                    _currentPrimaryId = primary.Id;
                    _primaryLockedUntilUtc = nowUtc + PrimaryStickiness;
                }
                else
                {
                    _currentPrimaryId = null;
                }
            }

            if (primary != null)
                primary.Role = TargetRole.Primary;

            // Secondary promotion: next best visible targets above threshold
            var secondaryCandidates = targets
                .Where(t => t != primary && t.IsVisible && t.Score >= SecondaryScoreThreshold)
                .OrderByDescending(t => t.Score)
                .Take(MaxSecondaryTargets);

            foreach (var s in secondaryCandidates)
                s.Role = TargetRole.Secondary;
        }
    }
}
