using System;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace RuriLib.Services
{
    public static class Info
    {
        public static string GetMachineID()
        {
            try
            {
                string cpuId = GetWmiInfo("Win32_Processor", "ProcessorId");
                string boardId = GetWmiInfo("Win32_BaseBoard", "SerialNumber");

                string combined = $"{cpuId}-{boardId}";

                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
                    StringBuilder builder = new StringBuilder();
                    for (int i = 0; i < bytes.Length; i++)
                    {
                        builder.Append(bytes[i].ToString("x2"));
                    }
                    return builder.ToString();
                }
            }
            catch
            {
                return Environment.MachineName + Environment.UserName;
            }
        }

        private static string GetWmiInfo(string className, string propertyName)
        {
            try
            {
                ManagementObjectSearcher searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
                foreach (ManagementObject obj in searcher.Get())
                {
                    return obj[propertyName]?.ToString() ?? "";
                }
            }
            catch { }
            return "";
        }
    }
}
