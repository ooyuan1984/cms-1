﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Datory;
using Microsoft.Extensions.Configuration;

namespace SiteServer.Abstractions.Tests
{
    public class SettingsManager : ISettingsManager
    {
        private readonly IConfiguration _config;
        public SettingsManager(IConfiguration config, string contentRootPath, string webRootPath)
        {
            _config = config;
            ContentRootPath = contentRootPath;
            WebRootPath = webRootPath;

            try
            {
                ProductVersion = Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

                PluginVersion = FileVersionInfo.GetVersionInfo(PathUtils.GetBinDirectoryPath("SS.CMS.Abstractions.dll")).ProductVersion;

                if (Assembly.GetEntryAssembly()
                    .GetCustomAttributes(typeof(TargetFrameworkAttribute), false)
                    .SingleOrDefault() is TargetFrameworkAttribute targetFrameworkAttribute)
                {
                    TargetFramework = targetFrameworkAttribute.FrameworkName;
                }
            }
            catch
            {
                // ignored
            }

            var menusPath = PathUtils.GetLangPath(contentRootPath, "en", "menus.yml");
            if (FileUtils.IsFileExists(menusPath))
            {
                Menus = YamlUtils.FileToObject<IList<Menu>>(menusPath);
            }
            var permissionsPath = PathUtils.GetLangPath(contentRootPath, "en", "permissions.yml");
            if (FileUtils.IsFileExists(permissionsPath))
            {
                Permissions = YamlUtils.FileToObject<PermissionsSettings>(permissionsPath);
            }
        }

        public string ContentRootPath { get; }
        public string WebRootPath { get; }
        public string ProductVersion { get; }
        public string PluginVersion { get; }
        public string TargetFramework { get; }
        public bool IsNightlyUpdate => _config.GetValue<bool>(nameof(IsNightlyUpdate));
        public bool IsProtectData => _config.GetValue<bool>(nameof(IsProtectData));
        public string SecurityKey => _config.GetValue<string>(nameof(SecurityKey)) ?? StringUtils.GetShortGuid().ToUpper();

        public DatabaseType DatabaseType =>
            TranslateUtils.ToEnum(_config.GetValue<string>("Database:Type"), DatabaseType.MySql);
        public string DatabaseConnectionString => IsProtectData ? Decrypt(_config.GetValue<string>("Database:ConnectionString")) : _config.GetValue<string>("Database:ConnectionString");
        public CacheType CacheType => CacheType.Parse(_config.GetValue<string>("Cache:Type"));
        public string CacheConnectionString => IsProtectData ? Decrypt(_config.GetValue<string>("Cache:ConnectionString")) : _config.GetValue<string>("Cache:ConnectionString");

        public IList<Menu> Menus { get; }
        public PermissionsSettings Permissions { get; }

        public string Encrypt(string inputString)
        {
            return WebConfigUtils.EncryptStringBySecretKey(inputString, SecurityKey);
        }

        public string Decrypt(string inputString)
        {
            return WebConfigUtils.DecryptStringBySecretKey(inputString, SecurityKey);
        }

        public async Task SaveSettingsAsync(bool isNightlyUpdate, bool isProtectData, string securityKey, DatabaseType databaseType, string databaseConnectionString, CacheType cacheType, string cacheConnectionString)
        {
            var path = PathUtils.Combine(ContentRootPath, Constants.ConfigFileName);

            var databaseConnectionStringValue = databaseConnectionString;
            var cacheConnectionStringValue = cacheConnectionString;
            if (isProtectData)
            {
                databaseConnectionStringValue = Encrypt(databaseConnectionStringValue);
                cacheConnectionStringValue = Encrypt(cacheConnectionString);
            }

            var json = $@"
{{
  ""IsNightlyUpdate"": {isNightlyUpdate.ToString().ToLower()},
  ""IsProtectData"": {isProtectData.ToString().ToLower()},
  ""SecurityKey"": ""{securityKey}"",
  ""Database"": {{
    ""Type"": ""{databaseType.GetValue()}"",
    ""ConnectionString"": ""{databaseConnectionStringValue}""
  }},
  ""Redis"": {{
    ""Type"": ""{cacheType.Value}"",
    ""ConnectionString"": ""{cacheConnectionStringValue}""
  }}
}}";

            await File.WriteAllTextAsync(path, json.Trim());
        }
    }
}
