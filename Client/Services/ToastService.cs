using Microsoft.JSInterop;

namespace IamApi.Client.Services;

public class ToastService
{
    private readonly IJSRuntime _js;

    public ToastService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task ShowSuccess(string message, string? title = null)
    {
        await _js.InvokeVoidAsync("showToast", message, "success", title ?? "Success");
    }

    public async Task ShowError(string message, string? title = null)
    {
        await _js.InvokeVoidAsync("showToast", message, "error", title ?? "Error");
    }

    public async Task ShowWarning(string message, string? title = null)
    {
        await _js.InvokeVoidAsync("showToast", message, "warning", title ?? "Warning");
    }

    public async Task ShowInfo(string message, string? title = null)
    {
        await _js.InvokeVoidAsync("showToast", message, "info", title ?? "Info");
    }
}
