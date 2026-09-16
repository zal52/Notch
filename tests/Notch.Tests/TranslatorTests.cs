using Notch.Modules.Translator;
using Notch.Services.Clipboard;
using Notch.Services.Contracts;
using Xunit;
namespace Notch.Tests;
public sealed class TranslatorTests
{
 [Fact]
 public async Task TranslatesCopiesAndInvalidatesOldResult()
 {
  await using var clipboard = new ClipboardService(new FakeTextClipboardPlatform(), new ClipboardHistory());
  await clipboard.StartAsync(default);
  var service = new FakeTranslation();
  using var vm = new TranslatorViewModel(service, clipboard);
  Assert.False(vm.TranslateCommand.CanExecute(null));
  vm.Input = "Hello";
  await vm.TranslateCommand.ExecuteAsync(null);
  Assert.Equal("Привет", vm.Result);
  Assert.Equal("ru", service.Request!.TargetLanguage);
  Assert.Equal("en", service.Request.SourceLanguage);
  await vm.CopyCommand.ExecuteAsync(null);
  Assert.Equal("Привет", ((ClipboardContent.Text)clipboard.History[0].Content).Value);
  vm.Input = "Goodbye";
  Assert.Empty(vm.Result);
  Assert.False(vm.CopyCommand.CanExecute(null));
 }
 [Fact]
 public async Task LateReplyCannotReplaceEditedText()
 {
  await using var clipboard = new ClipboardService(new FakeTextClipboardPlatform(), new ClipboardHistory());
  var response = new TaskCompletionSource<TranslationResult>();
  var service = new FakeTranslation { Response = response.Task };
  using var vm = new TranslatorViewModel(service, clipboard) { Input = "Hello" };
  var pending = vm.TranslateCommand.ExecuteAsync(null);
  vm.Input = "New text";
  response.SetResult(new("Old reply", "en"));
  await pending;
  Assert.Empty(vm.Result);
  Assert.False(vm.IsBusy);
 }
 [Fact]
 public async Task SwapUsesTranslatedTextAndProviderFailureIsRecoverable()
 {
  await using var clipboard = new ClipboardService(new FakeTextClipboardPlatform(), new ClipboardHistory());
  var service = new FakeTranslation();
  using var vm = new TranslatorViewModel(service, clipboard) { Input = "Hello", Source = TranslatorViewModel.SourceLanguages[1] };
  await vm.TranslateCommand.ExecuteAsync(null);
  vm.SwapCommand.Execute(null);
  Assert.Equal("Привет", vm.Input);
  Assert.Equal("en", vm.Target.Code);
  Assert.Equal("ru", vm.Source.Code);
  service.Response = Task.FromException<TranslationResult>(new System.Net.Http.HttpRequestException());
  await vm.TranslateCommand.ExecuteAsync(null);
  Assert.Empty(vm.Result);
  Assert.False(vm.IsBusy);
  Assert.True(vm.TranslateCommand.CanExecute(null));
 }
 private sealed class FakeTranslation : ITranslationService
 {
  public TranslationRequest? Request { get; private set; }
  public Task<TranslationResult> Response { get; set; } = Task.FromResult(new TranslationResult("Привет", "en"));
  public Task<TranslationResult> TranslateAsync(TranslationRequest request, CancellationToken token) { Request = request; return Response; }
 }
}

