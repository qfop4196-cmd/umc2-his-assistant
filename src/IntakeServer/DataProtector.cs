using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Encrypts records at rest with Windows DPAPI (LocalMachine scope + per-install entropy).
    /// Copied files cannot be decrypted on another computer. install-server.ps1 also restricts the folder ACL.
    /// </summary>
    internal sealed class DataProtector
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("U2I1");
        private readonly byte[] entropy;

        public DataProtector(string entropyPath)
        {
            if (File.Exists(entropyPath))
            {
                entropy = File.ReadAllBytes(entropyPath);
            }
            else
            {
                entropy = Tokens.RandomBytes(32);
                Directory.CreateDirectory(Path.GetDirectoryName(entropyPath));
                File.WriteAllBytes(entropyPath, entropy);
            }
            if (entropy.Length < 16) throw new InvalidOperationException("Tệp khóa dữ liệu bị hỏng: " + entropyPath);
        }

        public byte[] Protect(string text)
        {
            var plain = Encoding.UTF8.GetBytes(text);
            try
            {
                var cipher = ProtectedData.Protect(plain, entropy, DataProtectionScope.LocalMachine);
                var result = new byte[Magic.Length + cipher.Length];
                Buffer.BlockCopy(Magic, 0, result, 0, Magic.Length);
                Buffer.BlockCopy(cipher, 0, result, Magic.Length, cipher.Length);
                return result;
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }

        public string Unprotect(byte[] data)
        {
            if (data == null || data.Length <= Magic.Length) throw new InvalidDataException("Bản ghi rỗng.");
            for (var i = 0; i < Magic.Length; i++)
                if (data[i] != Magic[i]) throw new InvalidDataException("Sai định dạng bản ghi.");
            var cipher = new byte[data.Length - Magic.Length];
            Buffer.BlockCopy(data, Magic.Length, cipher, 0, cipher.Length);
            var plain = ProtectedData.Unprotect(cipher, entropy, DataProtectionScope.LocalMachine);
            try { return Encoding.UTF8.GetString(plain); }
            finally { Array.Clear(plain, 0, plain.Length); }
        }
    }
}
