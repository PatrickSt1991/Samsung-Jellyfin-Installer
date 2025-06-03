using Samsung_Jellyfin_Installer.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Markup;

namespace Samsung_Jellyfin_Installer.Converters
{
    [MarkupExtensionReturnType(typeof(BindingExpression))]
    public class LocalizeExtension : MarkupExtension
    {
        public string Key { get; set; }

        public LocalizeExtension() { }

        public LocalizeExtension(string key)
        {
            Key = key;
        }

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(Key))
                return null;

            var binding = new Binding($"[{Key}]")
            {
                Source = LocalizedStrings.Instance,
                Mode = BindingMode.OneWay
            };

            return binding.ProvideValue(serviceProvider);
        }
    }
    public static class StringExtensions
    {
        public static string Localized(this string key)
        {
            return LocalizedStrings.Instance[key];
        }
    }
}
