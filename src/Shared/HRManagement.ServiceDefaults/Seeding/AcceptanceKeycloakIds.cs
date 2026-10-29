using System.Security.Cryptography;
using System.Text;

namespace HRManagement.ServiceDefaults.Seeding;

/// <summary>
/// C# twin of <c>Get-AcceptanceKeycloakUserId</c> in <c>scripts/prepare-keycloak-import.ps1</c>: the acceptance realm
/// imports every persona with a deterministic Keycloak user id (the JWT <c>sub</c>), so seed data can name the creator
/// of a fixture (PLAN-33: creator SoD and work-inbox states).
/// </summary>
public static class AcceptanceKeycloakIds
{
    public static Guid UserId(string username)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"HRM|acceptance-v2|acceptance-v2|KEYCLOAK|{username}"));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>The Keycloak user id of an acceptance persona by persona id (e.g. <c>central-a</c>).</summary>
    public static Guid PersonaUserId(string personaId) => UserId(AcceptancePersonaCatalog.Get(personaId).Username);
}
