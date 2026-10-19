using System;
using System.Diagnostics;
using System.Threading;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using D3D11Device = SharpDX.Direct3D11.Device;

namespace DriverSetup
{
    /// <summary>
    /// GPU 负载：全屏 pixel shader 填充（每个像素跑 600 次三角运算），
    /// 不锁垂直同步，把显卡吃满。
    /// </summary>
    public class GpuBurner : IDisposable
    {
        private const string ShaderSource = @"
struct VSOut { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

VSOut VSMain(uint id : SV_VertexID)
{
    VSOut o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
    o.uv = uv;
    return o;
}

float4 PSMain(VSOut i) : SV_TARGET
{
    float2 p = i.uv * 4.0 - 2.0;
    float3 acc = 0;
    [loop] for (int k = 0; k < 600; k++)
    {
        p = sin(p * 1.017 + k * 0.0031) * cos(p.yx * 0.993 - k * 0.0027);
        acc += float3(p, dot(p, p));
    }
    return float4(frac(abs(acc) * 0.37), 1);
}";

        private Thread _thread;
        private volatile bool _running;
        private IntPtr _hwnd;
        private int _w, _h;

        public volatile bool IsRunning;
        public volatile float Fps;
        public string Error { get; private set; }

        public bool Start(IntPtr hwnd, int width, int height)
        {
            if (_running) return true;
            if (hwnd == IntPtr.Zero) { Error = "窗口句柄无效"; return false; }

            _hwnd = hwnd;
            _w = Math.Max(64, width);
            _h = Math.Max(64, height);
            _running = true;
            Error = null;

            _thread = new Thread(RenderLoop);
            _thread.IsBackground = true;
            _thread.Start();
            return true;
        }

        public void Stop()
        {
            _running = false;
            IsRunning = false;
            var t = _thread;
            if (t != null)
            {
                try { if (!t.Join(2000)) { } } catch { }
            }
            _thread = null;
        }

        private void RenderLoop()
        {
            D3D11Device device = null;
            SwapChain swap = null;
            DeviceContext ctx = null;
            RenderTargetView rtv = null;
            VertexShader vs = null;
            PixelShader ps = null;
            Texture2D backBuffer = null;

            try
            {
                var desc = new SwapChainDescription
                {
                    BufferCount = 2,
                    ModeDescription = new ModeDescription(_w, _h, new Rational(0, 1), Format.B8G8R8A8_UNorm),
                    IsWindowed = true,
                    OutputHandle = _hwnd,
                    SampleDescription = new SampleDescription(1, 0),
                    SwapEffect = SwapEffect.Discard,
                    Usage = Usage.RenderTargetOutput,
                    Flags = SwapChainFlags.None,
                };

                D3D11Device.CreateWithSwapChain(
                    DriverType.Hardware, DeviceCreationFlags.BgraSupport, desc,
                    out device, out swap);

                ctx = device.ImmediateContext;

                // 关掉 Alt+Enter 全屏切换
                using (var factory = swap.GetParent<Factory>())
                    factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAltEnter);

                backBuffer = Texture2D.FromSwapChain<Texture2D>(swap, 0);
                rtv = new RenderTargetView(device, backBuffer);

                using (var vsBytecode = ShaderBytecode.Compile(ShaderSource, "VSMain", "vs_5_0", ShaderFlags.OptimizationLevel3))
                    vs = new VertexShader(device, vsBytecode);
                using (var psBytecode = ShaderBytecode.Compile(ShaderSource, "PSMain", "ps_5_0", ShaderFlags.OptimizationLevel3))
                    ps = new PixelShader(device, psBytecode);

                ctx.VertexShader.Set(vs);
                ctx.PixelShader.Set(ps);
                ctx.OutputMerger.SetRenderTargets(rtv);
                ctx.Rasterizer.SetViewport(0, 0, _w, _h, 0f, 1f);

                IsRunning = true;
                var sw = Stopwatch.StartNew();
                int frames = 0;
                double lastFps = sw.Elapsed.TotalSeconds;

                while (_running)
                {
                    ctx.ClearRenderTargetView(rtv, new Color4(0f, 0f, 0f, 1f));
                    ctx.Draw(3, 0);                 // 全屏三角形
                    swap.Present(0, PresentFlags.None);   // 不锁垂直同步

                    frames++;
                    double now = sw.Elapsed.TotalSeconds;
                    if (now - lastFps >= 0.5)
                    {
                        Fps = (float)(frames / (now - lastFps));
                        frames = 0;
                        lastFps = now;
                    }
                }
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                IsRunning = false;
                _running = false;

                if (ctx != null) { try { ctx.ClearState(); ctx.Flush(); } catch { } }
                if (rtv != null) rtv.Dispose();
                if (backBuffer != null) backBuffer.Dispose();
                if (vs != null) vs.Dispose();
                if (ps != null) ps.Dispose();
                if (ctx != null) ctx.Dispose();
                if (swap != null) swap.Dispose();
                if (device != null) device.Dispose();
            }
        }

        public void Dispose() { Stop(); }
    }
}
