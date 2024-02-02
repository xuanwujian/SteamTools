// ReSharper disable once CheckNamespace
namespace BD.WTTS.Services;

public sealed partial class GameAcceleratorService
#if (WINDOWS || MACCATALYST || MACOS || LINUX) && !(IOS || ANDROID)
    : ReactiveObject
#endif
{
    static GameAcceleratorService? mCurrent;

    public static GameAcceleratorService Current => mCurrent ?? new();

    public SourceCache<XunYouGameViewModel, int> Games { get; }

    [Reactive]
    public IReadOnlyCollection<XunYouGame>? AllGames { get; set; }

    [Reactive]
    public bool IsLoadingGames { get; set; }

    [Reactive]
    public string? SearchText { get; set; }

    [Reactive]
    public XunYouGameViewModel? CurrentAcceleratorGame { get; set; }

    [Reactive]
    public XunYouAccelStateModel? XYAccelState { get; set; }

    public ICommand DeleteMyGameCommand { get; }

    public ICommand GameAcceleratorCommand { get; }

    public ICommand InstallAcceleratorCommand { get; }

    public ICommand UninstallAcceleratorCommand { get; }

    public ICommand AcceleratorChangeAreaCommand { get; }

    GameAcceleratorService()
    {
        Games = new(t => t.Id);

        this.WhenPropertyChanged(x => x.XYAccelState, false)
            .Subscribe(x =>
            {
                if (x.Value == null)
                    return;

                if (x.Value.State is XunYouState.加速已完成)
                {
                    if (x.Value.GameId != 0)
                    {
                        var game = AllGames?.FirstOrDefault(s => s.Id == x.Value.GameId);
                        if (game != null)
                        {
                            if (CurrentAcceleratorGame != null)
                            {
                                CurrentAcceleratorGame.IsAccelerating = false;
                                CurrentAcceleratorGame.IsAccelerated = true;
                                CurrentAcceleratorGame.LastAccelerateTime = DateTimeOffset.Now;
                            }
                            else
                            {
                                CurrentAcceleratorGame = new XunYouGameViewModel(game)
                                {
                                    IsAccelerated = true,
                                    LastAccelerateTime = DateTimeOffset.Now,
                                    IsAccelerating = false,
                                    SelectedArea = new XunYouGameArea() { Id = x.Value.AreaId },
                                    SelectedServer = new XunYouGameServer() { Id = x.Value.ServerId }
                                };

                                //Games.AddOrUpdate(CurrentAcceleratorGame);
                            }

                            //加速后
                            Toast.Show(ToastIcon.Success, "加速成功");
                            int testSpeedCallback(SpeedCallbackWrapper w)
                            {
                                CurrentAcceleratorGame.PingValue = w.Struct.PingSpeed;
                                CurrentAcceleratorGame.PingSpeedLoss = w.Struct.PingSpeedLoss;
                                Debug.WriteLine($"测速通知状态：{w.State},SpeedCallbackInfo: ErrorDesc/{w.ErrorDesc}, ErrorCode/{w.Struct.ErrorCode}, PingSpeed/{w.Struct.PingSpeed}, PingLocal/{w.Struct.PingLocal}, PingSpeedLoss/{w.Struct.PingSpeedLoss}, PingLocalLoss/{w.Struct.PingLocalLoss}");
                                return 0;
                            }
                            var speedCode = XunYouSDK.TestSpeed(CurrentAcceleratorGame.Id, CurrentAcceleratorGame.SelectedArea!.Id, testSpeedCallback);
#if DEBUG
                            if (speedCode == XunYouTestSpeedCode.成功)
                            {
                                Toast.Show(ToastIcon.Info, "发送测速指令");
                            }
#endif
                        }
                        else
                        {
                            RefreshAllGames();
                        }
                    }
                }
                else if (x.Value.State is XunYouState.未加速)
                {
                    //先停止测速
                    XunYouSDK.StopTestSpeded();

                    Games.Items.Where(s => s.IsAccelerating || s.IsAccelerated).ForEach(s =>
                    {
                        RestoreGameStatus(s);
                        Games.Refresh(s);
                    });
                    if (CurrentAcceleratorGame != null)
                    {
                        RestoreGameStatus(CurrentAcceleratorGame);
                        CurrentAcceleratorGame = null;
                    }
                }
                else
                {
                    Games.Items.Where(s => s.IsAccelerating || s.IsAccelerated).ForEach(s =>
                    {
                        RestoreGameStatus(s);
                        Games.Refresh(s);
                    });
                    if (CurrentAcceleratorGame != null)
                    {
                        RestoreGameStatus(CurrentAcceleratorGame);
                        CurrentAcceleratorGame = null;
                    }
                    Toast.Show(ToastIcon.Warning, x.Value.State.ToString());
                }
            });

        LoadGames();
        RefreshAllGames();
        DeleteMyGameCommand = ReactiveCommand.Create<XunYouGameViewModel>(DeleteMyGame);
        GameAcceleratorCommand = ReactiveCommand.Create<XunYouGameViewModel>(GameAccelerator);
        InstallAcceleratorCommand = ReactiveCommand.Create(InstallAccelerator);
        UninstallAcceleratorCommand = ReactiveCommand.Create(UninstallAccelerator);
        AcceleratorChangeAreaCommand = ReactiveCommand.Create<XunYouGameViewModel>(AcceleratorChangeArea);
    }

    /// <summary>
    /// 恢复加速游戏状态
    /// </summary>
    private static void RestoreGameStatus(XunYouGameViewModel app)
    {
        app.PingValue = 0;
        app.PingSpeedLoss = 0;
        app.IsAccelerating = false;
        app.IsAccelerated = false;
        app.IsStopAccelerating = false;
    }

    public async void GameAccelerator(XunYouGameViewModel app)
    {
        if (app.IsAccelerating)
            return;

        if (!app.IsAccelerated)
        {
            if (UserService.Current.User?.WattOpenId == null)
            {
                Toast.Show(ToastIcon.Warning, "需要登录账号才能使用游戏加速功能!");
                return;
            }
            var xunYouIsInstall = await IAcceleratorService.Instance.XY_IsInstall();
            if (xunYouIsInstall.HandleUI(out var isInstall))
            {
                if (!isInstall)
                {
                    if (!await WindowManagerService.Current.ShowTaskDialogAsync(
                                   new MessageBoxWindowViewModel() { Content = "需要下载 Watt 加速器插件才可使用，确定要下载吗?" }, title: "未安装加速插件", isCancelButton: true))
                    {
                        return;
                    }

                    await InstallAccelerator();
                }

                #region 通过 XY_GetAccelStateEx 判断是否已启动迅游

                //var accStateR = await IAcceleratorService.Instance.XY_GetAccelStateEx();
                //bool isStartXY = false;
                //if (accStateR.HandleUI(out var accStateResponse))
                //{
                //    isStartXY = accStateResponse.AccelState != XunYouAccelStateEx.获取失败;
                //    if (isStartXY && accStateResponse.State != XunYouState.未加速)
                //    {
                //        Toast.Show(ToastIcon.Warning, accStateResponse.State.ToString() ?? "失败");
                //        return;
                //    }
                //}

                #endregion

                #region 通过 XY_IsRunning 判断是否已启动迅游

                var apiRspIsRunning = await IAcceleratorService.Instance.XY_IsRunning();
                if (!apiRspIsRunning.HandleUI(out var isRunningCode))
                {
                    // 调用后端失败，显示错误并中止逻辑
                    return;
                }

                bool isStartXY = isRunningCode == XunYouIsRunningCode.加速器已启动;

                #endregion

                app.IsAccelerating = true;
                //加速中
                var gameInfo = XunYouSDK.GetGameInfo(app.Id);
                if (gameInfo == null)
                {
                    Toast.Show(ToastIcon.Warning, "获取游戏信息失败");
                    return;
                }
                app.GameInfo = gameInfo;
                var vm = new GameInfoPageViewModel(app);
                var result = await WindowManagerService.Current.ShowTaskDialogAsync(vm, $"{app.Name} - 区服选择", pageContent: new GameInfoPage(), isOkButton: false, disableScroll: true);
                if (!result || app.SelectedArea == null)
                {
                    app.IsAccelerating = false;
                    return;
                }

                if (!GameAcceleratorSettings.MyGames.ContainsKey(app.Id))
                {
                    GameAcceleratorSettings.MyGames.Add(app.Id, app);
                    Current.Games.AddOrUpdate(app);
                }

                //var progress = new Progress<int>();
                //progress.ProgressChanged += (sender, e) =>
                //{
                //    Debug.WriteLine($"安装迅游进度：{e} （InstallAsync）");
                //    if (e < 100)
                //    {
                //        app.AcceleratingProgress = e;
                //    }
                //    else
                //    {
                //        app.AcceleratingProgress = e;
                //    }
                //};

                //var startCode = await XunYouSDK.StartEx2Async(UserService.Current.User.WattOpenId, UserService.Current.User.NickName, app.Id, app.SelectedArea.Id, progress, GameAcceleratorSettings.WattAcceleratorDirPath.Value!, app.SelectedArea.Name);

                var start = isStartXY ? await IAcceleratorService.Instance.XY_StartAccel(app.Id, app.SelectedArea.Id, app.SelectedServer?.Id ?? 0, app.SelectedArea.Name)
                    : await IAcceleratorService.Instance.XY_StartEx2(UserService.Current.User.WattOpenId, UserService.Current.User.NickName, app.Id, app.SelectedArea.Id, app.SelectedArea.Name);
                if (start.HandleUI(out var startCode))
                {
                    if (startCode == 101)
                    {
                        app.AcceleratingProgress = 0;
                        CurrentAcceleratorGame = app;
                        Toast.Show(ToastIcon.Info, "正在加速中...");
                    }
                    else
                    {
                        Toast.Show(ToastIcon.Error, "加速启动失败");
                        app.IsAccelerating = false;
                    }
                }
                else
                {
                    app.IsAccelerating = false;
                }
            }
        }
        else
        {
            var stopRequest = await IAcceleratorService.Instance.XY_StopAccel();
            if (stopRequest.HandleUI(out var code))
            {
                // 停止加速
                if (code == XunYouSendResultCode.发送成功)
                {
                    app.IsAccelerating = true;
                    Toast.Show(ToastIcon.Info, "正在停止加速中...");
                    //CurrentAcceleratorGame = null;
                    //app.PingValue = 0;
                    //app.IsAccelerated = false;
                }
                else
                {
                    Toast.Show(ToastIcon.Error, "停止加速失败，请尝试去加速器客户端停止加速");
                }
            }
        }
    }

    public static void DeleteMyGame(XunYouGameViewModel app)
    {
        //if (await WindowManagerService.Current.ShowTaskDialogAsync(
        //    new MessageBoxWindowViewModel() { Content = Strings.Script_ReplaceTips }, isCancelButton: true, isDialog: false))
        //{
        //}
        Current.Games.RemoveKey(app.Id);
        GameAcceleratorSettings.MyGames.Remove(app.Id);
        Toast.Show(ToastIcon.Success, "已移除");
    }

    public static void AdddMyGame(XunYouGameViewModel app)
    {
        app.LastAccelerateTime = DateTimeOffset.Now;
        if (GameAcceleratorSettings.MyGames.TryAdd(app.Id, app))
        {
            Current.Games.AddOrUpdate(app);
            Toast.Show(ToastIcon.Success, "已添加到游戏列表");
        }
    }

    /// <summary>
    /// 加载迅游游戏数据
    /// </summary>
    public void LoadGames()
    {
        if (!XunYouSDK.IsSupported)
            return;

        Task2.InBackground(async () =>
        {
            if (!IsLoadingGames)
            {
                IsLoadingGames = true;
                if (GameAcceleratorSettings.MyGames.Any_Nullable())
                {
                    Games.Clear();
                    Games.AddOrUpdate(GameAcceleratorSettings.MyGames.Value!.Values);
                }
                else
                {
                    var games = XunYouSDK.GetHotGames();
                    if (games != null)
                    {
                        Games.Clear();
                        GameAcceleratorSettings.MyGames.AddRange(games.Select(s => new KeyValuePair<int, XunYouGameViewModel>(s.Id, new XunYouGameViewModel(s))));
                        Games.AddOrUpdate(GameAcceleratorSettings.MyGames.Value!.Values);
                    }
                }

                //判断是否已经在加速
                var accState = await IAcceleratorService.Instance.XY_GetAccelStateEx();
                if (accState.HandleUI() && accState.Content != null)
                {
                    if (accState.Content.GameId > 0 && accState.Content.State is XunYouState.加速已完成 or XunYouState.加速中)
                    {
                        var game = Games.Lookup(accState.Content.GameId);
                        if (game.HasValue)
                        {
                            game.Value.IsAccelerated = true;
                            game.Value.LastAccelerateTime = DateTime.Now;
                            var gameinfo = XunYouSDK.GetGameInfo(game.Value.Id);
                            game.Value.SelectedArea = gameinfo?.Areas?.FirstOrDefault(s => s.Id == accState.Content.AreaId);
                        }
                    }
                }

                IsLoadingGames = false;
            }
        });
    }

    public void RefreshAllGames()
    {
        Task2.InBackground(() =>
        {
            var games = XunYouSDK.GetAllGames();
            if (games != null)
            {
                AllGames = new ReadOnlyCollection<XunYouGame>(games);
            }
        });
    }

    public static async Task InstallAccelerator()
    {
        var xunYouIsInstall = await IAcceleratorService.Instance.XY_IsInstall();
        if (xunYouIsInstall.HandleUI(out var isInstall))
        {
            if (isInstall)
            {
                Toast.Show(ToastIcon.Info, "已安装Watt加速器");
                return;
            }
            var td = new TaskDialog
            {
                Title = "下载插件",
                ShowProgressBar = true,
                IconSource = new SymbolIconSource { Symbol = FluentAvalonia.UI.Controls.Symbol.Download },
                SubHeader = "下载 Watt 加速器 插件",
                Content = "正在初始化，请稍候",
                XamlRoot = WindowManagerService.GetWindowTopLevel(),
                //Buttons =
                //{
                //    new TaskDialogButton(Strings.Cancel, TaskDialogStandardResult.Cancel)
                //}
            };
            var install = IAcceleratorService.Instance.XY_Install(GameAcceleratorSettings.WattAcceleratorDirPath.Value!);

            td.Opened += async (s, e) =>
            {
                await foreach (var item in install)
                {
                    if (item.IsSuccess)
                    {
                        switch (item.Content)
                        {
                            case < 100:
                                Dispatcher.UIThread.Post(() => { td.Content = $"正在下载 {item.Content}%"; });
                                td.SetProgressBarState(item.Content, TaskDialogProgressState.Normal);
                                break;
                            case 100:
                                td.SetProgressBarState(item.Content, TaskDialogProgressState.Indeterminate);
                                Dispatcher.UIThread.Post(() => { td.Content = $"下载完成，正在安装..."; });
                                break;
                            case (int)XunYouDownLoadCode.安装成功:
                                //处理成功
                                //Dispatcher.UIThread.Post(() => { td.Content = $"安装完成"; });
                                Dispatcher.UIThread.Post(() => { td.Hide(TaskDialogStandardResult.OK); });
                                td.Hide();
                                break;
                            case int n when n > 101 && n < (int)XunYouDownLoadCode.启动安装程序失败:
                                //处理失败
                                break;
                            // Code 和进度重叠 递进 1000 XunYouInstallOrStartCode.默认 XunYouInstallOrStartCode.已安装
                            case 1000:
                                Dispatcher.UIThread.Post(() => { td.Content = $"默认"; });
                                // XunYouInstallOrStartCode.默认
                                break;
                            case 1001:
                                Dispatcher.UIThread.Post(() => { td.Content = $"已安装"; });
                                // XunYouInstallOrStartCode.已安装
                                Dispatcher.UIThread.Post(() => { td.Hide(TaskDialogStandardResult.OK); });
                                break;
                        }
                    }
                    else
                    {
                        //处理错误
                        Toast.Show(ToastIcon.Error, item.Message);
                    }
                }
            };

            //_ = Task.Run(() => { XunYouSDK.InstallAsync(progress, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WattAccelerator")); });

            var result = await td.ShowAsync(true);
        }
    }

    public static async void UninstallAccelerator()
    {
        var xunYouIsInstall = await IAcceleratorService.Instance.XY_IsInstall();
        if (xunYouIsInstall.HandleUI(out var isInstall))
        {
            if (!isInstall)
            {
                Toast.Show(ToastIcon.Info, "未安装，不需要卸载");
                return;
            }
            var uninstall = await IAcceleratorService.Instance.XY_Uninstall();
            if (uninstall.HandleUI(out var code))
            {
                if (code == 0)
                {
                    Toast.Show(ToastIcon.Success, "卸载成功");
                }
                else
                {
                    Toast.Show(ToastIcon.Error, "卸载失败");
                }
            }
        }
    }

    public async void AcceleratorChangeArea(XunYouGameViewModel app)
    {
        var gameInfo = XunYouSDK.GetGameInfo(app.Id);
        if (gameInfo == null)
        {
            Toast.Show(ToastIcon.Warning, "获取游戏信息失败");
            return;
        }
        app.GameInfo = gameInfo;
        var vm = new GameInfoPageViewModel(app);
        var result = await WindowManagerService.Current.ShowTaskDialogAsync(vm, $"{app.Name} - 区服选择", pageContent: new GameInfoPage(), isOkButton: false, disableScroll: true);
        if (!result || app.SelectedArea == null)
        {
            app.IsAccelerating = false;
            return;
        }

        if (app.IsAccelerated)
        {
            var start = await IAcceleratorService.Instance.XY_StartAccel(app.Id, app.SelectedArea.Id, app.SelectedServer?.Id ?? 0, app.SelectedArea.Name);
            if (start.HandleUI(out var startCode))
            {
                if (startCode == 101)
                {
                    app.IsAccelerating = true;
                    app.AcceleratingProgress = 0;
                    CurrentAcceleratorGame = app;
                    Toast.Show(ToastIcon.Info, "正在加速中...");
                }
                else
                {
                    Toast.Show(ToastIcon.Error, "加速启动失败");
                    app.IsAccelerating = false;
                }
            }
            else
            {
                app.IsAccelerating = false;
            }
        }
    }

    public async Task RefreshXYAccelState()
    {
        var result = await IAcceleratorService.Instance.XY_GetAccelStateEx();
        if (result.HandleUI(out var content))
        {
            XYAccelState = content;
        }
    }
}