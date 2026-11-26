using System.ComponentModel.DataAnnotations;

namespace NotificationApi.Web.Ingress;

/// <summary>HA Ingress settings (config section <c>Ingress</c>).</summary>
public sealed class IngressOptions
{
    public const string SectionName = "Ingress";

    /// <summary>The Supervisor's Ingress proxy. Only requests from this address may set Ingress headers.</summary>
    [Required]
    public string TrustedProxyAddress { get; set; } = "172.30.32.2";

    /// <summary>
    /// Development only: treat every request as coming from Ingress and inject fake identity headers.
    /// Startup fails if this is enabled outside the Development environment.
    /// </summary>
    public bool DevFakeIngress { get; set; }
}
