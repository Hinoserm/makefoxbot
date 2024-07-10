using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace admingui
{
    public partial class ImageListView : UserControl
    {
        private WebSocketManager _webSocketManager;
        private List<long> _imageIds;
        private int _currentPage;
        private const int PageSize = 20;
        private bool _loading;
        private long? _uid;
        private string? _type;
        private ImageList _imageList;

        public ImageListView()
        {
            InitializeComponent();
            _webSocketManager = WebSocketManager.Instance;
            _imageIds = new List<long>();
            _currentPage = 0;
            _loading = false;
            _imageList = new ImageList
            {
                ImageSize = new Size(100, 100),
                ColorDepth = ColorDepth.Depth32Bit
            };
            listView1.LargeImageList = _imageList;
            listView1.View = View.LargeIcon;
        }

        public async Task LoadList(long uid, string? type = null)
        {
            _uid = uid;
            _type = type;
            _currentPage = 0;
            _imageIds.Clear();
            _imageList.Images.Clear();
            listView1.Items.Clear();
            await LoadImagesAsync();
        }

        private async Task LoadImagesAsync()
        {
            if (_loading) return;

            _loading = true;

            try
            {
                var columns = new List<string> { "id", "status", "type", "uid", "date_added" };
                var images = await _webSocketManager.GetQueueItemsAsync(
                    pageNumber: _currentPage + 1,
                    pageSize: PageSize,
                    type: _type,
                    columns: columns,
                    uid: _uid
                );

                if (images.Any())
                {
                    foreach (var image in images)
                    {

                        var uid = image.ContainsKey("UID") ? image["UID"].ToString() : "N/A";
                        var type = image.ContainsKey("Type") ? image["Type"].ToString() : "N/A";
                        var status = image.ContainsKey("Status") ? image["Status"].ToString() : "N/A";

                        if (status != "FINISHED")
                            continue;

                        if (!image.ContainsKey("ImageID") && image["ImageID"] is not null)
                            continue; // Skip items without ImageID

                        try
                        {
                            var imageId = Convert.ToInt64(image["ImageID"].ToString());
                            _imageIds.Add(imageId);

                            // Load image thumbnail
                            var imageData = await _webSocketManager.GetImageAsync(imageId);
                            using (var ms = new System.IO.MemoryStream(imageData))
                            {
                                var img = Image.FromStream(ms);
                                _imageList.Images.Add(imageId.ToString(), img);
                            }

                            var item = new ListViewItem
                            {
                                Text = imageId.ToString(),
                                ImageKey = imageId.ToString()
                            };
                            listView1.Items.Add(item);
                        }
                        catch
                        {
                            // Skip invalid image
                        }
                    }

                    _currentPage++;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load images: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            _loading = false;
        }
    }
}
