﻿/*
* Copyright (C) Sportradar AG. See LICENSE for full license governing this code
*/
using Sportradar.OddsFeed.SDK.Common;
using Sportradar.OddsFeed.SDK.Common.Internal;

namespace Sportradar.OddsFeed.SDK.API.Internal.Config
{
    internal class ConfigurationBuilder : RecoveryConfigurationBuilder<IConfigurationBuilder>, IConfigurationBuilder
    {
        /// <summary>
        /// A <see cref="SdkEnvironment"/> instance specifying the selected environment
        /// </summary>
        private readonly SdkEnvironment _environment;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationBuilder"/> class
        /// </summary>
        /// <param name="accessToken">An access token used to authenticate with the feed</param>
        /// <param name="sectionProvider">A <see cref="IConfigurationSectionProvider"/> used to access <see cref="IOddsFeedConfigurationSection"/></param>
        /// <param name="environment">An <see cref="SdkEnvironment"/> specifying the selected environment</param>
        public ConfigurationBuilder(string accessToken, IConfigurationSectionProvider sectionProvider, SdkEnvironment environment)
            : base(accessToken, sectionProvider)
        {
            _environment = environment;
        }

        public override IOddsFeedConfiguration Build()
        {
            PreBuildCheck();

            var mqHost = string.IsNullOrEmpty(Section?.Host)
                             ? EnvironmentManager.GetMqHost(_environment)
                             : Section.Host;
            var apiHost = string.IsNullOrEmpty(Section?.ApiHost)
                             ? EnvironmentManager.GetApiHost(_environment)
                             : Section.ApiHost;

            var config = new OddsFeedConfiguration(AccessToken,
                                                   _environment,
                                                   DefaultLocale,
                                                   SupportedLocales,
                                                   mqHost,
                                                   null,
                                                   EnvironmentManager.DefaultMqHostPort,
                                                   null,
                                                   null,
                                                   apiHost,
                                                   true,
                                                   true,
                                                   InactivitySeconds ?? SdkInfo.MinInactivitySeconds,
                                                   MaxRecoveryTimeInSeconds ?? SdkInfo.MaxRecoveryExecutionInSeconds,
                                                   MinIntervalBetweenRecoveryRequests ?? SdkInfo.DefaultIntervalBetweenRecoveryRequests,
                                                   NodeId,
                                                   DisabledProducers,
                                                   ExceptionHandlingStrategy,
                                                   AdjustAfterAge ?? false,
                                                   HttpClientTimeout ?? SdkInfo.DefaultHttpClientTimeout,
                                                   RecoveryHttpClientTimeout ?? HttpClientTimeout ?? SdkInfo.DefaultHttpClientTimeout,
                                                   Section);

            return config;
        }
    }
}
