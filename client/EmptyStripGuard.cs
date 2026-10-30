using System;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

namespace myseq
{
    /// <summary>
    /// DockPanelSuite 3.1.1 draws a tool window's tab strip from a rectangle that is the strip's height
    /// minus 1. A strip with nothing to show (a pane with a single tab) has no height, so if anything asks
    /// it to paint or print, GDI+ is handed a rectangle of height -1 and throws "Rectangle ... cannot have
    /// a width or height equal to 0", which Windows Forms reports as an unhandled-exception dialog.
    /// There is nothing to draw in a strip that small, so this ignores paint and print requests for it.
    /// </summary>
    internal sealed class EmptyStripGuard : NativeWindow
    {
        private const int WM_PAINT = 0x000F;
        private const int WM_PRINT = 0x0317;
        private const int WM_PRINTCLIENT = 0x0318;

        // The tool-window strip leaves 1 pixel at the bottom, so anything this small has no tab area.
        private const int MinDrawableSize = 3;

        private readonly Control strip;

        public EmptyStripGuard(Control strip)
        {
            this.strip = strip;
            strip.HandleCreated += (s, e) => AssignHandle(strip.Handle);
            strip.HandleDestroyed += (s, e) => ReleaseHandle();
            if (strip.IsHandleCreated)
                AssignHandle(strip.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            if ((m.Msg == WM_PAINT || m.Msg == WM_PRINT || m.Msg == WM_PRINTCLIENT) && IsTooSmallToDraw())
            {
                if (m.Msg == WM_PAINT)
                    DefWndProc(ref m);      // validates the update region without calling OnPaint
                else
                    m.Result = IntPtr.Zero; // nothing to print
                return;
            }

            base.WndProc(ref m);
        }

        private bool IsTooSmallToDraw()
        {
            return strip.ClientSize.Height < MinDrawableSize || strip.ClientSize.Width < MinDrawableSize;
        }
    }

    /// <summary>Creates the theme's normal tab strips and puts an <see cref="EmptyStripGuard"/> on each.</summary>
    internal sealed class GuardedDockPaneStripFactory : DockPanelExtender.IDockPaneStripFactory
    {
        private readonly DockPanelExtender.IDockPaneStripFactory inner;

        public GuardedDockPaneStripFactory(DockPanelExtender.IDockPaneStripFactory inner)
        {
            this.inner = inner;
        }

        public DockPaneStripBase CreateDockPaneStrip(DockPane pane)
        {
            DockPaneStripBase strip = inner.CreateDockPaneStrip(pane);
            new EmptyStripGuard(strip);
            return strip;
        }
    }
}
