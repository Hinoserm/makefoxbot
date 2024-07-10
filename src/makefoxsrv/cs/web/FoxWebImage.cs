using EmbedIO;
using EmbedIO.WebSockets;
using MySqlConnector;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TL;
using System.Text.Json.Nodes;

using JsonObject = System.Text.Json.Nodes.JsonObject;
using JsonArray = System.Text.Json.Nodes.JsonArray;
using System.CodeDom;

namespace makefoxsrv
{
    public class FoxWebImage
    {
        [WebFunctionName("Get")]
        [WebLoginRequired(true)]
        [WebAccessLevel(AccessLevel.BASIC)]
        public static async Task<JsonObject?> Get(FoxWebSession session, JsonObject jsonMessage)
        {
            long imageId = FoxJsonHelper.GetLong(jsonMessage, "ImageID", false).Value;
            int? maxSize = FoxJsonHelper.GetInt(jsonMessage, "MaxSize", true);

            var image = await FoxImage.Load((ulong)imageId);

            if (image == null || image.Image == null)
            {
                return new JsonObject
                {
                    ["Command"] = "Image:Get",
                    ["Success"] = false,
                    ["Error"] = "Image not found."
                };
            }

            // For now, we're not resizing the image, just returning it as is.
            // You can add resizing logic here later if needed.
            var response = new JsonObject
            {
                ["Command"] = "Image:Get",
                ["Success"] = true,
                ["ImageID"] = image.ID,
                ["Type"] = image.Type.ToString(),
                ["Filename"] = image.Filename,
                ["DateAdded"] = image.DateAdded,
                ["Image"] = Convert.ToBase64String(image.Image)
            };

            return response;
        }
    }
}
