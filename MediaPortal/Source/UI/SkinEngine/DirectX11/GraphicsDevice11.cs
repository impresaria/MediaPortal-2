﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.UI.SkinEngine.ContentManagement;
using MediaPortal.UI.SkinEngine.DirectX;
using MediaPortal.UI.SkinEngine.DirectX.RenderPipelines;
using MediaPortal.UI.SkinEngine.DirectX.RenderStrategy;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.Direct3D11;
using SharpDX.Direct3D;
using SharpDX.DXGI;
using FeatureLevel = SharpDX.Direct3D.FeatureLevel;
using Format = SharpDX.DXGI.Format;
using PresentFlags = SharpDX.DXGI.PresentFlags;
using SwapChain = SharpDX.DXGI.SwapChain;
using SwapEffect = SharpDX.DXGI.SwapEffect;
using Usage = SharpDX.DXGI.Usage;

namespace MediaPortal.UI.SkinEngine.DirectX11
{
  internal class GraphicsDevice11 : IDisposable
  {
    private SharpDX.Direct3D11.Device _device3D;
    private SharpDX.Direct3D11.Device1 _device3D1;
    private SharpDX.DXGI.Device _deviceDXGI;
    private SharpDX.Direct2D1.Device _device2D1;
    private readonly D3DSetup _setup = new D3DSetup();

    private SwapChain _swapChain;
    private Texture2D _backBufferTexture;
    private Surface1 _backBuffer;
    private SharpDX.Direct2D1.DeviceContext _context2D;
    private Bitmap1 _renderTarget2D;

    private readonly ReaderWriterLockSlim _renderAndResourceAccessLock = new ReaderWriterLockSlim();

    private static GraphicsDevice11 _instance;

    public static GraphicsDevice11 Instance
    {
      get { return _instance ?? (_instance = new GraphicsDevice11()); }
    }

    public Form RenderTarget { get; internal set; }

    public Surface1 BackBuffer
    {
      get { return _backBuffer; }
    }

    public Texture2D BackBufferTexture
    {
      get { return _backBufferTexture; }
    }

    public SharpDX.Direct3D11.Device Device3D
    {
      get { return _device3D; }
    }

    public SharpDX.Direct3D11.Device1 Device3D1
    {
      get { return _device3D1; }
    }

    public SharpDX.DXGI.Device DeviceDXGI
    {
      get { return _deviceDXGI; }
    }

    public SharpDX.Direct2D1.Device Device2D1
    {
      get { return _device2D1; }
    }

    public SharpDX.Direct2D1.DeviceContext Context2D1
    {
      get { return _context2D; }
    }

    public Bitmap1 RenderTarget2D
    {
      get { return _renderTarget2D; }
    }

    public RenderPassType RenderPass { get; set; }

    // Render process related events
    public event EventHandler DeviceSceneBegin;
    public event EventHandler DeviceSceneEnd;
    public event EventHandler DeviceScenePresented;

    // RenderModeType related fields
    private int _currentRenderStrategyIndex = 0;
    private List<IRenderStrategy> _renderStrategies;

    // RenderPipeline related fields
    private int _currentRenderPipeplineIndex;
    private List<IRenderPipeline> _renderPipelines;

    /// <summary>
    /// Initializes or re-initializes the DirectX device and the backbuffer. This is necessary in the initialization phase
    /// of the SkinEngine and after a parameter was changed which affects the DX device creation.
    /// </summary>
    /// <remarks>
    /// This method has to be called from the main application thread because the DirectX device will be created by this method.
    /// </remarks>
    /// <param name="window">The window which is being used as render target; that window will contain the DX device.</param>
    internal void Initialize_MainThread(Form window)
    {
      RenderTarget = window;
      CreateDevice();
    }

    public void CreateDevice()
    {
      // SwapChain description
      int width = RenderTarget.ClientSize.Width;
      int height = RenderTarget.ClientSize.Height;
      var desc = new SwapChainDescription
      {
        BufferCount = 1,
        ModeDescription = new ModeDescription(width, height, new Rational(50, 1), Format.R8G8B8A8_UNorm),
        IsWindowed = true,
        OutputHandle = RenderTarget.Handle,
        SampleDescription = new SampleDescription(1, 0),
        SwapEffect = SwapEffect.Discard,
        Usage = Usage.RenderTargetOutput
      };

      // Create Device and SwapChain
      var flags = DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport;
      FeatureLevel[] featureLevels =
      {
        FeatureLevel.Level_9_1,
        FeatureLevel.Level_9_2,
        FeatureLevel.Level_9_3,
        FeatureLevel.Level_10_0,
        FeatureLevel.Level_10_1,
        FeatureLevel.Level_11_0,
        FeatureLevel.Level_11_1
      };

      SharpDX.Direct3D11.Device.CreateWithSwapChain(DriverType.Hardware, flags, featureLevels, desc, out _device3D, out _swapChain);

      // New RenderTargetView from the backbuffer
      _backBufferTexture = Texture2D.FromSwapChain<Texture2D>(_swapChain, 0);
      _backBuffer = _backBufferTexture.QueryInterface<Surface1>();

      _device3D1 = _device3D.QueryInterface<SharpDX.Direct3D11.Device1>(); // get a reference to the Direct3D 11.1 device
      _deviceDXGI = _device3D1.QueryInterface<SharpDX.DXGI.Device>(); // get a reference to DXGI device

      _device2D1 = new SharpDX.Direct2D1.Device(_deviceDXGI); // initialize the D2D device

      _context2D = new SharpDX.Direct2D1.DeviceContext(_device2D1, DeviceContextOptions.EnableMultithreadedOptimizations);

      _renderTarget2D = new Bitmap1(_context2D, _backBuffer);
      _context2D.Target = _renderTarget2D;

      SetupRenderPipelines();
      SetupRenderStrategies();
    }

    /// <summary>
    /// Setups all <see cref="IRenderStrategy"/>s.
    /// </summary>
    private void SetupRenderStrategies()
    {
      _renderStrategies = new List<IRenderStrategy>
        {
          new Default(_setup), 
          new VSync(_setup), 
          new MaxPerformance(_setup)
        };
      _currentRenderStrategyIndex = 0;
    }

    /// <summary>
    /// Setups all <see cref="IRenderPipeline"/>s.
    /// </summary>
    private void SetupRenderPipelines()
    {
      _renderPipelines = new List<IRenderPipeline>
        {
          new SinglePass2DRenderPipeline(),
          new SBSRenderPipeline(),
          new TABRenderPipeline(),
          new SBS2DRenderPipeline(),
          new TAB2DRenderPipeline(),
        };
      _currentRenderPipeplineIndex = 0;
    }

    /// <summary>
    /// Gets the current <see cref="IRenderStrategy"/>.
    /// </summary>
    public IRenderStrategy RenderStrategy
    {
      get { return _renderStrategies[_currentRenderStrategyIndex]; }
    }

    /// <summary>
    /// Switches through all possible RenderStrategies.
    /// </summary>
    public void NextRenderStrategy()
    {
      _currentRenderStrategyIndex = (_currentRenderStrategyIndex + 1) % _renderStrategies.Count;
    }

    /// <summary>
    /// Gets the current <see cref="IRenderPipeline"/>.
    /// </summary>
    public IRenderPipeline RenderPipeline
    {
      get { return _renderPipelines[_currentRenderPipeplineIndex]; }
    }

    /// <summary>
    /// Switches through all possible RenderPipelines.
    /// </summary>
    public void NextRenderPipeline()
    {
      _currentRenderPipeplineIndex = (_currentRenderPipeplineIndex + 1) % _renderPipelines.Count;
    }

    /// <summary>
    /// Fires an event if listeners are available.
    /// </summary>
    /// <param name="eventHandler"></param>
    private void Fire(EventHandler eventHandler)
    {
      try
      {
        if (eventHandler != null)
          eventHandler(null, EventArgs.Empty);
      }
      catch (Exception e)
      {
        ServiceRegistration.Get<ILogger>().Error("Error executing render event handler:", e);
      }
    }

    /// <summary>
    /// Renders the entire scene.
    /// </summary>
    /// <param name="doWaitForNextFame"><c>true</c>, if this method should wait to the correct frame start time
    /// before it renders, else <c>false</c>.</param>
    /// <returns><c>true</c>, if the caller should wait some milliseconds before rendering the next time.</returns>
    public bool Render(bool doWaitForNextFame)
    {
      if (_device2D1 == null)
        return true;

      IRenderStrategy renderStrategy = RenderStrategy;
      IRenderPipeline pipeline = RenderPipeline;

      renderStrategy.BeginRender(doWaitForNextFame);

      _renderAndResourceAccessLock.EnterReadLock();
      try
      {
        Fire(DeviceSceneBegin);

        pipeline.BeginRender();

        pipeline.Render();

        pipeline.EndRender();

        Fire(DeviceSceneEnd);

        _swapChain.Present(1, PresentFlags.None);
        //_device.PresentEx(renderStrategy.PresentMode);

        Fire(DeviceScenePresented);

        ContentManager.Instance.Clean();
      }
      catch (SharpDXException e)
      {
        ServiceRegistration.Get<ILogger>().Warn("GraphicsDevice: DirectX Exception", e);
        return false;
      }
      finally
      {
        _renderAndResourceAccessLock.ExitReadLock();
      }
      return false;
    }

    private static void TryDispose<TE>(ref TE disposable)
    {
      IDisposable disp = disposable as IDisposable;
      if (disp != null)
        disp.Dispose();
      disposable = default(TE);
    }

    public void Dispose()
    {
      TryDispose(ref _backBuffer);
      TryDispose(ref _backBufferTexture);
      TryDispose(ref _swapChain);

      TryDispose(ref _renderTarget2D);
      TryDispose(ref _context2D);

      TryDispose(ref _device3D1);
      TryDispose(ref _device3D);
      TryDispose(ref _device2D1);
      TryDispose(ref _deviceDXGI);
    }
  }
}