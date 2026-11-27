namespace NotificationApi.Domain.Recipients;

/// <summary>Why a member can't be added to a group.</summary>
public enum MembershipProblem
{
    /// <summary>It's already a member of this group (a contact can still be in many groups).</summary>
    AlreadyMember,

    /// <summary>A group can't contain itself.</summary>
    Self,

    /// <summary>The candidate already contains this group, directly or through other groups (AC55).</summary>
    Cycle,
}
