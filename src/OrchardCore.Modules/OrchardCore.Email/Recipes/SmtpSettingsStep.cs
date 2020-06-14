using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using OrchardCore.Deployment;
using OrchardCore.Email.Services;
using OrchardCore.Entities;
using OrchardCore.Recipes.Models;
using OrchardCore.Recipes.Services;
using OrchardCore.Settings;

namespace OrchardCore.Email.Recipes
{
    public class SmtpSettingsStep : IRecipeStepHandler
    {
        private readonly ISiteService _siteService;
        private readonly IDataProtectionProvider _dataProtectionProvider;
        private ILogger _logger;

        public SmtpSettingsStep(ISiteService siteService,
            IDataProtectionProvider dataProtectionProvider,
            ILogger<SmtpSettingsStep> logger
            )
        {
            _siteService = siteService;
            _dataProtectionProvider = dataProtectionProvider;
            _logger = logger;
        }

        public async Task ExecuteAsync(RecipeExecutionContext context)
        {
            if (!string.Equals(context.Name, "SmtpSettings", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var site = await _siteService.LoadSiteSettingsAsync();
            var jSettings = site.Properties["SmtpSettings"] as JObject;
            if (jSettings != null)
            {
                jSettings.Merge(context.Step["SmtpSettings"]);
            }

            var model = jSettings.ToObject<SmtpSettings>();

            var userName = context.Properties.SelectToken("SmtpSettings.UserName")?.ToObject<Property>();

            var passwordProperty = context.Properties.SelectToken("SmtpSettings.Password")?.ToObject<Property>();

            if (passwordProperty != null)
            {
                var value = passwordProperty.Value?.ToString();
                switch (passwordProperty.Handler)
                {
                    case PropertyHandler.UserSupplied:
                        if (!String.IsNullOrEmpty(value))
                        {
                            // encrypt the password
                            model.Password = EncryptPassword(value);
                        }
                        else
                        {
                            _logger.LogError("User supplied setting 'Password' not provided for 'SmtpSettings'");
                        }
                        break;
                    case PropertyHandler.PlainText:
                        // encrypt the password
                        model.Password = EncryptPassword(value);
                        break;
                    case PropertyHandler.Encrypted:
                        // Decrypt the password
                        if (!String.IsNullOrEmpty(value))
                        {
                            try
                            {
                                var protector = _dataProtectionProvider.CreateProtector(nameof(SmtpSettingsConfiguration));
                                protector.Unprotect(value);
                                model.Password = value;
                            }
                            catch
                            {
                                _logger.LogError("The Smtp password could not be decrypted. It may have been encrypted using a different key.");
                            }
                        }
                        break;
                    default:
                        break;

                }
            }

            site.Put(model);
            await _siteService.UpdateSiteSettingsAsync(site);
        }

        private string EncryptPassword(string password)
        {
            var protector = _dataProtectionProvider.CreateProtector(nameof(SmtpSettingsConfiguration));
            return protector.Protect(password);
        }
    }
}
