using System;
using System.Security.Cryptography;

namespace DotDesk.Core.Models
{
    /// <summary>
    /// 一次性临时密码（6位随机字母数字）
    /// 被控端生成并显示，控制端输入后验证
    /// </summary>
    public sealed class OneTimePassword
    {
        private string _password;
        private readonly object _lock = new();

        public OneTimePassword()
        {
            _password = Generate();
        }

        /// <summary>当前密码</summary>
        public string Current
        {
            get { lock (_lock) return _password; }
        }

        /// <summary>刷新生成新密码</summary>
        public string Refresh()
        {
            lock (_lock)
            {
                _password = Generate();
                return _password;
            }
        }

        /// <summary>验证密码（忽略空格和连字符）</summary>
        public bool Verify(string input)
        {
            var clean = input?.Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();
            lock (_lock) return clean == _password;
        }

        // ── 生成6位随机密码（数字+小写字母，去掉易混淆字符）────────
        // 字符集去掉：0/o/O（易混淆）、1/l/I（易混淆）
        private static readonly char[] _chars =
            "23456789abcdefghjkmnpqrstuvwxyz".ToCharArray();

        private static string Generate()
        {
            var bytes = RandomNumberGenerator.GetBytes(6);
            var result = new char[6];
            for (int i = 0; i < 6; i++)
                result[i] = _chars[bytes[i] % _chars.Length];
            return new string(result);
        }
    }
}
