using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DeskInk.Core.Transport;

public sealed class LanPairingIdentity : IDisposable
{
    public const string HostIdentityExtensionOid = "1.3.6.1.4.1.57264.1.1";
    private LanPairingIdentity(X509Certificate2 certificate)
    {
        Certificate = certificate;
        CertificateFingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        ComparisonCode = $"{CertificateFingerprint[..4]}-{CertificateFingerprint[4..8]}";
    }

    public X509Certificate2 Certificate { get; }
    public string CertificateFingerprint { get; }
    public string ComparisonCode { get; }

    public static LanPairingIdentity Create(TimeSpan? lifetime = null)
    {
        var effectiveLifetime = lifetime ?? TimeSpan.FromHours(24);
        if (effectiveLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var request = new CertificateRequest(
                "CN=DeskInk LAN Pairing",
                signingKey,
                HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                true));
            var enhancedUsage = new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1", "TLS Web Server Authentication"),
            };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(enhancedUsage, true));
            request.CertificateExtensions.Add(new X509Extension(
                new Oid(HostIdentityExtensionOid, "DeskInk LAN Host Identity"),
                [0x05, 0x00],
                critical: false));
            var now = DateTimeOffset.UtcNow;
            using var ephemeralCertificate = request.CreateSelfSigned(
                now.AddMinutes(-5),
                now.Add(effectiveLifetime));
            var pkcs12 = ephemeralCertificate.Export(X509ContentType.Pkcs12);
            X509Certificate2 certificate;
            try
            {
                certificate = X509CertificateLoader.LoadPkcs12(
                    pkcs12,
                    password: null,
                    X509KeyStorageFlags.UserKeySet |
                    X509KeyStorageFlags.PersistKeySet |
                    X509KeyStorageFlags.Exportable);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pkcs12);
            }
            signingKey.Dispose();
            return new LanPairingIdentity(certificate);
        }
        catch
        {
            signingKey.Dispose();
            throw;
        }
    }

    public byte[] CreateSessionSecret() =>
        RandomNumberGenerator.GetBytes(LanAuthenticatedDatagram.SessionSecretBytes);

    public static LanPairingIdentity FromCertificate(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey) throw new CryptographicException("LAN certificate has no private key");
        if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            throw new CryptographicException("LAN certificate is expired");
        return new LanPairingIdentity(certificate);
    }

    public void Dispose()
    {
        Certificate.Dispose();
    }
}
