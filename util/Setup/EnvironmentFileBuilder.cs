﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Bit.Core.Utilities;

namespace Bit.Setup
{
    public class EnvironmentFileBuilder
    {
        private readonly Context _context;

        private IDictionary<string, string> _globalValues;
        private IDictionary<string, string> _mssqlValues;
        private IDictionary<string, string> _globalOverrideValues;
        private IDictionary<string, string> _mssqlOverrideValues;

        public EnvironmentFileBuilder(Context context)
        {
            _context = context;
            _globalValues = new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["globalSettings__baseServiceUri__vault"] = "http://localhost",
                ["globalSettings__baseServiceUri__api"] = "http://localhost/api",
                ["globalSettings__baseServiceUri__identity"] = "http://localhost/identity",
                ["globalSettings__baseServiceUri__admin"] = "http://localhost/admin",
                ["globalSettings__baseServiceUri__notifications"] = "http://localhost/notifications",
                ["globalSettings__baseServiceUri__internalNotifications"] = "http://notifications:5000",
                ["globalSettings__baseServiceUri__internalAdmin"] = "http://admin:5000",
                ["globalSettings__baseServiceUri__internalIdentity"] = "http://identity:5000",
                ["globalSettings__baseServiceUri__internalApi"] = "http://api:5000",
                ["globalSettings__baseServiceUri__internalVault"] = "http://web:5000",
            };
            _mssqlValues = new Dictionary<string, string>
            {
                ["ACCEPT_EULA"] = "Y",
                ["MSSQL_PID"] = "Express",
                ["SA_PASSWORD"] = "SECRET",
            };
        }

        public void BuildForInstaller()
        {
            Directory.CreateDirectory("/bitwarden/env/");
            Init();
            Build();
        }

        public void BuildForUpdater()
        {
            Init();
            
            LoadExistingValues(_globalOverrideValues, "/bitwarden/env/global.override.env");
            LoadExistingValues(_mssqlOverrideValues, "/bitwarden/env/mssql.override.env");

            Update();
            Build();
        }

        private void Init()
        {
            var dbPassword = _context.Stub ? "RANDOM_DATABASE_PASSWORD" : CoreHelpers.SecureRandomString(32);
            var dbConnectionString = new SqlConnectionStringBuilder
            {
                DataSource = "tcp:mssql,1433",
                InitialCatalog = "vault",
                UserID = "sa",
                Password = dbPassword,
                MultipleActiveResultSets = false,
                Encrypt = true,
                ConnectTimeout = 30,
                TrustServerCertificate = true,
                PersistSecurityInfo = false
            }.ConnectionString;

            _globalOverrideValues = new Dictionary<string, string>
            {
                ["globalSettings__baseServiceUri__vault"] = _context.Config.Url,
                ["globalSettings__baseServiceUri__api"] = $"{_context.Config.Url}/api",
                ["globalSettings__baseServiceUri__identity"] = $"{_context.Config.Url}/identity",
                ["globalSettings__baseServiceUri__admin"] = $"{_context.Config.Url}/admin",
                ["globalSettings__baseServiceUri__notifications"] = $"{_context.Config.Url}/notifications",
                ["globalSettings__sqlServer__connectionString"] = $"\"{dbConnectionString}\"",
                ["globalSettings__identityServer__certificatePassword"] = _context.Install?.IdentityCertPassword,
                ["globalSettings__attachment__baseDirectory"] = $"{_context.OutputDir}/core/attachments",
                ["globalSettings__attachment__baseUrl"] = $"{_context.Config.Url}/attachments",
                ["globalSettings__dataProtection__directory"] = $"{_context.OutputDir}/core/aspnet-dataprotection",
                ["globalSettings__logDirectory"] = $"{_context.OutputDir}/logs",
                ["globalSettings__licenseDirectory"] = $"{_context.OutputDir}/core/licenses",
                ["globalSettings__internalIdentityKey"] = _context.Stub ? "RANDOM_IDENTITY_KEY" :
                    CoreHelpers.SecureRandomString(64, alpha: true, numeric: true),
                ["globalSettings__duo__aKey"] = _context.Stub ? "RANDOM_DUO_AKEY" :
                    CoreHelpers.SecureRandomString(64, alpha: true, numeric: true),
                ["globalSettings__installation__id"] = _context.Install?.InstallationId.ToString(),
                ["globalSettings__installation__key"] = _context.Install?.InstallationKey,
                ["globalSettings__yubico__clientId"] = "REPLACE",
                ["globalSettings__yubico__key"] = "REPLACE",
                ["globalSettings__mail__replyToEmail"] = $"no-reply@{_context.Config.Domain}",
                ["globalSettings__mail__smtp__host"] = "REPLACE",
                ["globalSettings__mail__smtp__port"] = "587",
                ["globalSettings__mail__smtp__ssl"] = "false",
                ["globalSettings__mail__smtp__username"] = "REPLACE",
                ["globalSettings__mail__smtp__password"] = "REPLACE",
                ["globalSettings__disableUserRegistration"] = "false",
                ["globalSettings__hibpApiKey"] = "REPLACE",
                ["adminSettings__admins"] = string.Empty,
            };

            _mssqlOverrideValues = new Dictionary<string, string>
            {
                ["ACCEPT_EULA"] = "Y",
                ["MSSQL_PID"] = "Express",
                ["SA_PASSWORD"] = dbPassword,
            };
        }

        private void LoadExistingValues(IDictionary<string, string> _values, string file)
        {
            if(!File.Exists(file))
            {
                return;
            }

            var fileLines = File.ReadAllLines(file);
            foreach(var line in fileLines)
            {
                if(!line.Contains("="))
                {
                    continue;
                }

                var value = string.Empty;
                var lineParts = line.Split("=", 2);
                if(lineParts.Length < 1)
                {
                    continue;
                }

                if(lineParts.Length > 1)
                {
                    value = lineParts[1];
                }

                if(_values.ContainsKey(lineParts[0]))
                {
                    _values[lineParts[0]] = value;
                }
                else
                {
                    _values.Add(lineParts[0], value);
                }
            }
        }

        private void Update()
        {
            var disableUserRegistration = !_context.Config.EnableUserRegistration;
            
            _globalOverrideValues["globalSettings__disableUserRegistration"] =
                disableUserRegistration.ToString().ToLower();
            _globalOverrideValues["adminSettings__admins"] = string.Join(",",
                _context.Config.Admins.DefaultIfEmpty());
        }

        private void Build()
        {
            var template = Helpers.ReadTemplate("EnvironmentFile");

            Helpers.WriteLine(_context, "Building docker environment files.");
            Directory.CreateDirectory("/bitwarden/docker/");
            using(var sw = File.CreateText("/bitwarden/docker/global.env"))
            {
                sw.Write(template(new TemplateModel(_globalValues)));
            }
            Helpers.Exec("chmod 600 /bitwarden/docker/global.env");

            using(var sw = File.CreateText("/bitwarden/docker/mssql.env"))
            {
                sw.Write(template(new TemplateModel(_mssqlValues)));
            }
            Helpers.Exec("chmod 600 /bitwarden/docker/mssql.env");

            Helpers.WriteLine(_context, "Building docker environment override files.");
            Directory.CreateDirectory("/bitwarden/env/");
            using(var sw = File.CreateText("/bitwarden/env/global.override.env"))
            {
                sw.Write(template(new TemplateModel(_globalOverrideValues)));
            }
            Helpers.Exec("chmod 600 /bitwarden/env/global.override.env");

            using(var sw = File.CreateText("/bitwarden/env/mssql.override.env"))
            {
                sw.Write(template(new TemplateModel(_mssqlOverrideValues)));
            }
            Helpers.Exec("chmod 600 /bitwarden/env/mssql.override.env");

            // Empty uid env file. Only used on Linux hosts.
            if(!File.Exists("/bitwarden/env/uid.env"))
            {
                using(var sw = File.CreateText("/bitwarden/env/uid.env")) { }
            }
        }

        public class TemplateModel
        {
            public TemplateModel(IEnumerable<KeyValuePair<string, string>> variables)
            {
                Variables = variables.Select(v => new Kvp { Key = v.Key, Value = v.Value });
            }

            public IEnumerable<Kvp> Variables { get; set; }

            public class Kvp
            {
                public string Key { get; set; }
                public string Value { get; set; }
            }
        }
    }
}
