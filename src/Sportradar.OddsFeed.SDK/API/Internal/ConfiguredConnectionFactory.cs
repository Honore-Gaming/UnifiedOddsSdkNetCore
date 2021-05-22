/*
* Copyright (C) Sportradar AG. See LICENSE for full license governing this code
*/
using System;
using Dawn;
using System.Net.Security;
using System.Security.Authentication;
using RabbitMQ.Client;
using Sportradar.OddsFeed.SDK.Common.Internal;

namespace Sportradar.OddsFeed.SDK.API.Internal
{
    /// <summary>
    /// A <see cref="IConnectionFactory"/> implementations which properly configures it self before first <see cref="IConnection"/> is created
    /// </summary>
    internal class ConfiguredConnectionFactory : IDisposable
    {
        /// <summary>
        /// A <see cref="IOddsFeedConfigurationInternal"/> instance containing configuration information
        /// </summary>
        private readonly IOddsFeedConfigurationInternal _config;

        /// <summary>
        /// A <see cref="ConnectionFactory"/> instance
        /// </summary>
        private readonly ConnectionFactory _connectionFactory;

        /// <summary>
        /// A singleton instance of <see cref="IConnection"/> class created by current factory.
        /// </summary>
        private IConnection _connectionSingleton;

        /// <summary>
        /// A <see cref="object"/> used to ensure thread safety when creating the connection singleton
        /// </summary>
        private readonly object _syncLock = new object();

        /// <summary>
        /// Gets the connection created date
        /// </summary>
        /// <value>The connection created date</value>
        public DateTime ConnectionCreated { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfiguredConnectionFactory"/> class
        /// </summary>
        /// <param name="config">A <see cref="IOddsFeedConfigurationInternal"/> instance containing configuration information</param>
        /// <param name="connectionFactory">A <see cref="ConnectionFactory"/> instance</param>
        public ConfiguredConnectionFactory(IOddsFeedConfigurationInternal config, ConnectionFactory connectionFactory)
        {
            Guard.Argument(config, nameof(config)).NotNull();

            _config = config;
            ConnectionCreated = DateTime.MinValue;
            
            _connectionFactory = connectionFactory;
        }

        /// <summary>
        /// Configures the current <see cref="ConfiguredConnectionFactory"/> based on config options read from <code>_config</code> field
        /// </summary>
        private void Configure()
        {
            _connectionFactory.HostName = _config.Host;
            _connectionFactory.Port = _config.Port;
            _connectionFactory.UserName = _config.Username;
            _connectionFactory.Password = _config.Password;
            _connectionFactory.VirtualHost = _config.VirtualHost;
            _connectionFactory.AutomaticRecoveryEnabled = true;

            _connectionFactory.Ssl.Enabled = _config.UseSsl;
            _connectionFactory.Ssl.Version = SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;
            if (_config.UseSsl)
            {
                _connectionFactory.Ssl.AcceptablePolicyErrors = SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable;
            }

            _connectionFactory.ClientProperties.Add("SrUfSdkType", ".netstd");
            _connectionFactory.ClientProperties.Add("SrUfSdkVersion", SdkInfo.GetVersion());
            _connectionFactory.ClientProperties.Add("SrUfSdkInit", $"{DateTime.Now:yyyyMMddHHmm}");
            _connectionFactory.ClientProperties.Add("SrUfSdkConnName", "RabbitMQ / NETStd");
            _connectionFactory.ClientProperties.Add("SrUfSdkBId", $"{_config.BookmakerDetails?.BookmakerId}");
        }

        /// <summary>
        /// Create a connection to the specified endpoint or return existing one
        /// </summary>
        /// <exception cref="T:RabbitMQ.Client.Exceptions.BrokerUnreachableException">When the configured host name was not reachable</exception>
        public IConnection CreateConnection()
        {
            lock (_syncLock)
            {
                if (_connectionSingleton == null)
                {
                    if (!_connectionFactory.ClientProperties.ContainsKey("SrUfSdkType"))
                    {
                        Configure(); // configure only the first time, even if disconnect happens
                    }
                    _connectionSingleton = _connectionFactory.CreateConnection();
                    ConnectionCreated = DateTime.Now;
                }
                return _connectionSingleton;
            }
        }

        public bool IsConnected()
        {
            lock (_syncLock)
            {
                return _connectionSingleton != null && _connectionSingleton.IsOpen;
            }
        }

        public void CloseConnection()
        {
            lock (_syncLock)
            {
                if (_connectionSingleton != null)
                {
                    _connectionSingleton.Close();
                    _connectionSingleton.Dispose();
                    _connectionSingleton = null;
                    ConnectionCreated = DateTime.MinValue;
                }
            }
        }

        /// <summary>Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.</summary>
        public void Dispose()
        {
            lock (_syncLock)
            {
                _connectionSingleton?.Close();
                _connectionSingleton?.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
