using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv
{
    internal class FoxPremium
    {
        public async Task<string[]?> CheckSettingsArePremium(FoxUserSettings settings)
        {
            // List to store premium features
            List<string> premiumFeatures = new List<string>();

            if (settings.width > 1088)
                premiumFeatures.Add("width > 1088");

            if (settings.height > 1088)
                premiumFeatures.Add("height > 1088");

            if (settings.steps > 20)
                premiumFeatures.Add("steps > 20");



            // Return the array of premium features if any, or null if none
            return premiumFeatures.Count > 0 ? premiumFeatures.ToArray() : null;
        }
    }
}
