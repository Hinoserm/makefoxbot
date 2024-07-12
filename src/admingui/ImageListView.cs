using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Threading;

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
        private SemaphoreSlim _imageLoadingSemaphore;

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
            listView1.Scrollable = true;
            listView1.RetrieveVirtualItem += ListView1_RetrieveVirtualItem;
            listView1.VirtualMode = true;
            listView1.VirtualListSize = 0;
            listView1.CacheVirtualItems += ListView1_CacheVirtualItems;
            _imageLoadingSemaphore = new SemaphoreSlim(1); // Limit concurrent image loading to 5
        }

        private async void ListView1_CacheVirtualItems(object sender, CacheVirtualItemsEventArgs e)
        {
            if (_loading || e.EndIndex < listView1.VirtualListSize - 1)
                return;

            await LoadImagesAsync();
        }

        private void ListView1_RetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            var imageId = _imageIds[e.ItemIndex].ToString();
            e.Item = new ListViewItem
            {
                Text = imageId,
                ImageKey = imageId
            };
        }

        public async Task LoadList(long uid, string? type = null)
        {
            _uid = uid;
            _type = type;
            _currentPage = 0;
            _imageIds.Clear();
            _imageList.Images.Clear();
            listView1.VirtualListSize = 0;
            listView1.Invalidate();
            await LoadImagesAsync();
        }

        private async Task LoadImagesAsync()
        {
            if (_loading) return;

            _loading = true;

            try
            {
                var columns = new List<string> { "id", "status", "type", "uid", "date_added", "ImageID" };
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
                        var status = image.ContainsKey("Status") ? image["Status"].ToString() : "N/A";

                        if (status != "FINISHED")
                            continue;

                        if (!image.ContainsKey("ImageID") || image["ImageID"] is null)
                            continue; // Skip items without ImageID

                        var imageId = Convert.ToInt64(image["ImageID"].ToString());
                        _imageIds.Add(imageId);

                        // Populate the list view with image ID
                        listView1.VirtualListSize = _imageIds.Count;
                    }

                    _currentPage++;
                    listView1.VirtualListSize = _imageIds.Count;
                    listView1.Invalidate();

                    // Load images in batches of 5
                    await LoadImagesInBatchesAsync(_imageIds);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load images: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            _loading = false;
        }

        private async Task LoadImagesInBatchesAsync(List<long> imageIds)
        {
            int batchSize = 5;
            for (int i = 0; i < imageIds.Count; i += batchSize)
            {
                var batch = imageIds.Skip(i).Take(batchSize).ToList();
                var tasks = batch.Select(imageId => LoadImageAsync(imageId)).ToList();
                await Task.WhenAll(tasks);

                // Redraw the entire list view after each batch is loaded
                Invoke(new Action(() =>
                {
                    listView1.Invalidate();
                }));
            }
        }

        private async Task LoadImageAsync(long imageId)
        {
            await _imageLoadingSemaphore.WaitAsync();
            try
            {
                var imageData = await _webSocketManager.GetImageAsync(imageId);
                using (var ms = new System.IO.MemoryStream(imageData))
                {
                    var img = Image.FromStream(ms);
                    _imageList.Images.Add(imageId.ToString(), img);

                    Invoke(new Action(() =>
                    {
                        var index = _imageIds.IndexOf(imageId);
                        if (index >= 0)
                        {
                            listView1.RedrawItems(index, index, true);
                        }
                    }));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image {imageId}: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _imageLoadingSemaphore.Release();
            }
        }



        private List<int> GetVisibleIndices()
        {
            List<int> visibleIndices = new List<int>();
            Rectangle clientRect = listView1.ClientRectangle;

            for (int i = 0; i < listView1.Items.Count; i++)
            {
                Rectangle itemRect = listView1.GetItemRect(i);
                if (clientRect.IntersectsWith(itemRect))
                {
                    visibleIndices.Add(i);
                }
            }

            return visibleIndices;
        }
    }
}
