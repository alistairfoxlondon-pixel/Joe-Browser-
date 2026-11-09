using System;
using System.Threading;
using System.Windows.Forms;

namespace JoeBrowser
{
    internal static class TestHarness
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            AppPaths.EnsureAll();

            var p = new Profile();
            p.Name = "Selftest";
            p.BrowserKind = "auto";
            FingerprintFactory.Regenerate(p.Fp, "Windows 11", null);
            Console.WriteLine("FP tz=" + p.Fp.Timezone + " lang=" + p.Fp.Language + " screen=" + p.Fp.ScreenW + "x" + p.Fp.ScreenH + " cores=" + p.Fp.Cores);

            BrowserSession s = null;
            try
            {
                s = BrowserSession.LaunchAsync(p, "https://example.com", "").Result;
                Thread.Sleep(4000);

                string js = @"(async () => {
  const mk = () => { const c = document.createElement('canvas'); c.width = 200; c.height = 40; const x = c.getContext('2d'); x.fillStyle = '#f60'; x.fillRect(0, 0, 200, 40); x.fillStyle = '#069'; x.font = '16px Arial'; x.fillText('Joe', 2, 20); return c; };
  const c = mk();
  const u = c.toDataURL('image/png').slice(-28);
  const b = await new Promise(r => c.toBlob(r, 'image/png'));
  const t = await new Promise(r => { const f = new FileReader(); f.onload = () => r(String(f.result).slice(-28)); f.readAsDataURL(b); });
  const lc = document.createElement('canvas'); lc.width = 200; lc.height = 40; const lg = lc.getContext('webgl');
  const tail = (s2) => s2.slice(-24);
  let glu = 'err', glb = 'err', glp = 'err';
  if (lg) {
    lg.clearColor(0.4, 0.6, 0.2, 1); lg.clear(lg.COLOR_BUFFER_BIT);
    const px = new Uint8Array(200 * 40 * 4); lg.readPixels(0, 0, 200, 40, lg.RGBA, lg.UNSIGNED_BYTE, px);
    glp = tail(String(Array.prototype.slice.call(px, 0, 40)));
    glu = tail(lc.toDataURL('image/png'));
    const gb = await new Promise(r => lc.toBlob(r, 'image/png'));
    glb = tail(await new Promise(r => { const f = new FileReader(); f.onload = () => r(String(f.result)); f.readAsDataURL(gb); }));
  }
  return JSON.stringify({
    url: u, blob: t, match: u === t,
    toBlobSrc: String(HTMLCanvasElement.prototype.toBlob).slice(0, 70),
    toBlobLen: HTMLCanvasElement.prototype.toBlob.length,
    rpSrc: String(WebGLRenderingContext.prototype.readPixels).slice(0, 70),
    glURL: glu, glBlob: glb, glURLisBlob: glu === glb, glPix: glp
  });
})()";

                string val = s.EvaluateAsync(js).Result;
                Console.WriteLine("RESULT: " + val);

                string u = s.EvaluateAsync("location.href + ' | title=' + document.title").Result;
                Console.WriteLine("PAGE: " + u);
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: " + ex);
            }
            finally
            {
                try { if (s != null) s.Close(); } catch { }
            }
        }
    }
}
