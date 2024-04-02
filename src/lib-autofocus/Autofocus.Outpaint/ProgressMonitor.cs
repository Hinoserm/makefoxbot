namespace Autofocus.Outpaint
{
    internal class ProgressMonitor
    {
        public event Func<ProgressReport, Task>? ProgressEvent;

        private readonly IStableDiffusion _api;
        private float _progress;

        public ProgressMonitor(IStableDiffusion api)
        {
            _api = api;
        }

        public Task Report(float value, Base64EncodedImage? intermediate = null)
        {
            _progress = Math.Max(value, _progress);

            return ProgressEvent?.Invoke(new ProgressReport(_progress, intermediate))
                ?? Task.CompletedTask;
        }

        public async Task<T> Report<T>(float start, float end, Task<T> task)
        {
            start = Math.Max(start, _progress);
            end = Math.Max(start, end);

            await Report(start);

            while (!task.IsCompleted)
            {
                try
                {
                    var value = await _api.Progress(true);
                    await Report(start + (end - start) * (float)value.Progress);
                }
                catch (TimeoutException)
                {

                }
                finally
                {
                    await Task.Delay(350);
                }
            }

            await Report(end);

            return await task;
        }
    }
}
