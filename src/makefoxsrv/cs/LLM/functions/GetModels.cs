using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace makefoxsrv.llm.functions
{
    internal class ImageModelInfo
    {
        [LLMFunction("Helps you find the best image model to meet the user's request.  Use only when one of your default models isn't up for the job.  Returns suggested models along with descriptions for each model.")]
        public static async Task<List<(string modelName, string description)>> SearchImageModels(
            FoxTelegram t,
            FoxUser user,
            [LLMParam("A short list of keywords to help the search engine. Can be left null for an extensive search.")] List<string>? keywords
        )
        {

            var models = FoxModel.GetAvailableModels();

            if (models.Count() == 0)
                throw new Exception("There are no models available.  The image server might be offline.");

            var results = new List<(string, string)>();

            foreach (var model in models)
            {
                var name = model.Name;
                var desc = model.Description ?? "(no description)";
                results.Add((name, desc));
            }

            return results;
        }

        [LLMFunction("This function causes a system crash for testing purposes.")]
        public static List<(string modelName, string description)> Crash(
            FoxTelegram t,
            FoxUser user
        )
        {
            throw new Exception("This is a test crash.");
        }
    }
}
