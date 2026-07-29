using System;
using System.Security.Cryptography;
using Animus.Models;

namespace Animus.Services;

/// <summary>Login e troca de senha dos dois usuarios do app.</summary>
public sealed class AuthService
{
    private const int Iterations = 120_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    public const int MinPasswordLength = 3;

    // Usuarios de fabrica: o login e a senha inicial sao iguais.
    private static readonly (string Id, string Password)[] Defaults =
    {
        ("kwvn", "kwvn"),
        ("saluty", "saluty"),
    };

    private readonly AppDataStore _store;

    public AuthService(AppDataStore store)
    {
        _store = store;
        SeedDefaults();
    }

    private void SeedDefaults()
    {
        var changed = false;
        foreach (var (id, password) in Defaults)
        {
            if (_store.Data.Users.ContainsKey(id)) continue;
            _store.Data.Users[id] = CreateUser(id, password);
            changed = true;
        }
        if (changed) _store.Save();
    }

    private static StoredUser CreateUser(string id, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new StoredUser
        {
            DisplayName = id,
            Salt = Convert.ToBase64String(salt),
            Hash = Hash(password, salt),
        };
    }

    private static string Hash(string password, byte[] salt)
    {
        var bytes = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return Convert.ToBase64String(bytes);
    }

    private static bool Verify(StoredUser user, string password)
    {
        byte[] salt;
        try { salt = Convert.FromBase64String(user.Salt); }
        catch { return false; }

        var candidate = Hash(password, salt);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(candidate),
            Convert.FromBase64String(user.Hash));
    }

    /// <summary>Valida login e senha. Retorna a conta quando der certo.</summary>
    public bool TryLogin(string login, string password, out UserAccount? account, out string error)
    {
        account = null;
        error = "";

        login = (login ?? "").Trim();
        password ??= "";

        if (login.Length == 0 || password.Length == 0)
        {
            error = "PREENCHA USUÁRIO E SENHA";
            return false;
        }

        if (!_store.Data.Users.TryGetValue(login, out var stored) || !Verify(stored, password))
        {
            error = "ACESSO NEGADO";
            return false;
        }

        account = new UserAccount(login.ToLowerInvariant(), stored.DisplayName);
        return true;
    }

    /// <summary>Troca a senha do usuario logado, exigindo a senha atual.</summary>
    public bool TryChangePassword(string userId, string currentPassword, string newPassword, string confirmPassword, out string error)
    {
        error = "";

        if (!_store.Data.Users.TryGetValue(userId, out var stored))
        {
            error = "Usuário não encontrado.";
            return false;
        }

        if (!Verify(stored, currentPassword ?? ""))
        {
            error = "Senha atual incorreta.";
            return false;
        }

        newPassword ??= "";
        if (newPassword.Length < MinPasswordLength)
        {
            error = $"A nova senha precisa ter pelo menos {MinPasswordLength} caracteres.";
            return false;
        }

        if (newPassword != confirmPassword)
        {
            error = "A confirmação não confere com a nova senha.";
            return false;
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        stored.Salt = Convert.ToBase64String(salt);
        stored.Hash = Hash(newPassword, salt);
        _store.Save();
        return true;
    }
}
