using System;
using System.Collections.Generic;

namespace Shadowsocks.Encryption
{
    public static class EncryptorFactory
    {
        private static readonly Dictionary<string, Type> _registeredEncryptors;

        private static readonly Type[] _constructorTypes = {typeof(string), typeof(string), typeof(bool), typeof(bool)};

        static EncryptorFactory()
        {
            _registeredEncryptors = new Dictionary<string, Type>();
            foreach (var method in PolarSSLEncryptor.SupportedCiphers())
            {
                _registeredEncryptors.Add(method, typeof(PolarSSLEncryptor));
            }
            foreach (var method in SodiumEncryptor.SupportedCiphers())
            {
                _registeredEncryptors.Add(method, typeof(SodiumEncryptor));
            }
        }

        public static IEncryptor GetEncryptor(string method, string password, bool onetimeauth, bool isudp)
        {
            if (method.IsNullOrEmpty())
            {
                method = "aes-256-cfb";
            }
            method = method.ToLowerInvariant();
            var t = _registeredEncryptors[method];
            var c = t.GetConstructor(_constructorTypes);
            var result = (IEncryptor) c.Invoke(new object[] {method, password, onetimeauth, isudp});
            return result;
        }
    }
}