using System.Security.Cryptography.X509Certificates;
using DeskInk.Core.Transport;

namespace DeskInk.WindowsHost;

internal static class LanHostIdentityStore
{
    private static readonly TimeSpan IdentityLifetime = TimeSpan.FromDays(3650);

    public static LanPairingIdentity LoadOrCreate()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var certificate = store.Certificates
            .Where(IsDeskInkIdentity)
            .Where(item => item.HasPrivateKey && item.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(30))
            .OrderByDescending(item => item.NotAfter)
            .FirstOrDefault();
        if (certificate is not null)
        {
            Console.WriteLine("LAN IDENTITY:     loaded from CurrentUser certificate store");
            return LanPairingIdentity.FromCertificate(certificate);
        }

        var identity = LanPairingIdentity.Create(IdentityLifetime);
        identity.Certificate.FriendlyName = "DeskInk LAN Host Identity";
        store.Add(identity.Certificate);
        Console.WriteLine("LAN IDENTITY:     created in CurrentUser certificate store");
        return identity;
    }

    private static bool IsDeskInkIdentity(X509Certificate2 certificate) =>
        certificate.Extensions.Cast<X509Extension>()
            .Any(extension => extension.Oid?.Value == LanPairingIdentity.HostIdentityExtensionOid);
}
