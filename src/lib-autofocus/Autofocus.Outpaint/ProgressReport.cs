namespace Autofocus.Outpaint;

public record struct ProgressReport(float Progress, Base64EncodedImage? Intermediate);