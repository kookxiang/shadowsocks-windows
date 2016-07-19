using System.Text;

namespace Shadowsocks.Encryption
{
    public abstract class EncryptorBase
        : IEncryptor
    {
        public const int MAX_INPUT_SIZE = 32768;
        protected bool IsUDP;

        protected string Method;
        protected bool OnetimeAuth;
        protected string Password;

        protected EncryptorBase(string method, string password, bool onetimeauth, bool isudp)
        {
            Method = method;
            Password = password;
            OnetimeAuth = onetimeauth;
            IsUDP = isudp;
        }

        public abstract void Encrypt(byte[] buf, int length, byte[] outbuf, out int outlength);

        public abstract void Decrypt(byte[] buf, int length, byte[] outbuf, out int outlength);

        public abstract void Dispose();

        protected byte[] GetPasswordHash()
        {
            var inputBytes = Encoding.UTF8.GetBytes(Password);
            var hash = MbedTLS.MD5(inputBytes);
            return hash;
        }
    }
}