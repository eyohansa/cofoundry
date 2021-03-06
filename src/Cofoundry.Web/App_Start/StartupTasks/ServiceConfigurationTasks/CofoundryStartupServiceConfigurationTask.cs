﻿using Cofoundry.Core;
using Cofoundry.Domain;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cofoundry.Web
{
    /// <summary>
    /// Extends the IMvcBuilder configuration to allow for modular configuration
    /// of Mvc services
    /// </summary>
    public class CofoundryStartupServiceConfigurationTask : IStartupServiceConfigurationTask
    {
        private readonly IEnumerable<IMvcJsonOptionsConfiguration> _mvcJsonOptionsConfigurations;
        private readonly IEnumerable<IMvcOptionsConfiguration> _mvcOptionsConfigurations;
        private readonly IEnumerable<IRazorViewEngineOptionsConfiguration> _razorViewEngineOptionsConfigurations;
        private readonly IUserAreaDefinitionRepository _userAreaDefinitionRepository;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly AuthenticationSettings _authenticationSettings;

        public CofoundryStartupServiceConfigurationTask(
            IEnumerable<IMvcJsonOptionsConfiguration> mvcJsonOptionsConfigurations,
            IEnumerable<IMvcOptionsConfiguration> mvcOptionsConfigurations,
            IEnumerable<IRazorViewEngineOptionsConfiguration> razorViewEngineOptionsConfigurations,
            IUserAreaDefinitionRepository userAreaDefinitionRepository,
            IHostingEnvironment hostingEnvironment,
            AuthenticationSettings authenticationSettings
            )
        {
            _mvcJsonOptionsConfigurations = mvcJsonOptionsConfigurations;
            _mvcOptionsConfigurations = mvcOptionsConfigurations;
            _razorViewEngineOptionsConfigurations = razorViewEngineOptionsConfigurations;
            _userAreaDefinitionRepository = userAreaDefinitionRepository;
            _hostingEnvironment = hostingEnvironment;
            _authenticationSettings = authenticationSettings;
        }

        /// <summary>
        /// Configures Mvc services. Runs after AddMvc in the service
        /// configuration pipeline.
        /// </summary>
        /// <param name="mvcBuilder">IMvcBuilder to configure.</param>
        public void ConfigureServices(IMvcBuilder mvcBuilder)
        {
            ConfigureAuth(mvcBuilder);

            foreach (var config in EnumerableHelper
                .Enumerate(_mvcJsonOptionsConfigurations)
                .OrderByDescending(o => o is CofoundryMvcJsonOptionsConfiguration))
            {
                mvcBuilder.Services.Configure<MvcJsonOptions>(o => config.Configure(o));
            }

            foreach (var config in EnumerableHelper.Enumerate(_mvcOptionsConfigurations))
            {
                mvcBuilder.Services.Configure<MvcOptions>(o => config.Configure(o));
            }

            foreach (var config in EnumerableHelper
                .Enumerate(_razorViewEngineOptionsConfigurations)
                .OrderByDescending(o => o is CofoundryRazorViewEngineOptionsConfiguration))
            {
                mvcBuilder.Services.Configure<RazorViewEngineOptions>(o => config.Configure(o));
            }
        }

        private void ConfigureAuth(IMvcBuilder mvcBuilder)
        {
            var services = mvcBuilder.Services;
            var allUserAreas = _userAreaDefinitionRepository.GetAll();

            // Set default schema as specified in config, falling back to CofoundryAdminUserArea
            // Since any additional areas are configured by the implementor there shouldn't be multiple
            // unless the developer has misconfigured their areas.
            var defaultSchemaCode = allUserAreas
                .OrderByDescending(u => u.IsDefaultAuthSchema)
                .ThenByDescending(u => u is CofoundryAdminUserArea)
                .ThenBy(u => u.Name)
                .Select(u => u.UserAreaCode)
                .First();

            var defaultScheme = CofoundryAuthenticationConstants.FormatAuthenticationScheme(defaultSchemaCode);

            var authBuilder = mvcBuilder.Services.AddAuthentication(defaultScheme);

            foreach (var userAreaDefinition in allUserAreas)
            {
                var scheme = CofoundryAuthenticationConstants.FormatAuthenticationScheme(userAreaDefinition.UserAreaCode);

                authBuilder
                    .AddCookie(scheme, cookieOptions =>
                    {
                        cookieOptions.Cookie.Name = GetCookieNamespace() + userAreaDefinition.UserAreaCode;
                        cookieOptions.Cookie.HttpOnly = true;

                        if (!string.IsNullOrWhiteSpace(userAreaDefinition.LoginPath))
                        {
                            cookieOptions.LoginPath = userAreaDefinition.LoginPath;
                        }
                    });
            }
        }

        private string GetCookieNamespace()
        {
            if (!string.IsNullOrWhiteSpace(_authenticationSettings.CookieNamespace))
            {
                return _authenticationSettings.CookieNamespace;
            }

            // Try and build a short and somewhat unique name using the 
            // application name, which should suffice for most scenarios. 

            var appName = _hostingEnvironment.ApplicationName;

            var reasonablyUniqueName = appName
                .Take(3)
                .Union(appName.Reverse())
                .Take(6);

            return "CFA_" + string.Concat(reasonablyUniqueName);
        }
    }
}
