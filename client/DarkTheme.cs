using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace myseq
{
    /// <summary>
    /// Dark colors for the client's windows. SetEnabled switches every open window; Apply themes a
    /// window that was just created. Colors the user picked on purpose (map colors, the swatches in
    /// the options dialog) are left alone: only controls that still use a system color are changed.
    /// </summary>
    internal static class DarkTheme
    {
        public static readonly Color WindowBack = Color.FromArgb(30, 30, 30);
        public static readonly Color PanelBack = Color.FromArgb(45, 45, 48);
        public static readonly Color InputBack = Color.FromArgb(37, 37, 38);
        public static readonly Color ButtonBack = Color.FromArgb(63, 63, 70);
        public static readonly Color ButtonHover = Color.FromArgb(80, 80, 88);
        public static readonly Color Border = Color.FromArgb(85, 85, 90);
        public static readonly Color Text = Color.FromArgb(230, 230, 230);
        public static readonly Color DisabledText = Color.FromArgb(130, 130, 130);

        // Colors for text typed into a box and for its placeholder, in either theme.
        // Color == also compares names, so a black picked in a color dialog is not Color.Black.
        public static bool IsBlack(Color color) { return color.ToArgb() == Color.Black.ToArgb(); }
        public static bool IsWhite(Color color) { return color.ToArgb() == Color.White.ToArgb(); }

        public static Color InputText { get { return Enabled ? Text : SystemColors.WindowText; } }
        public static Color PlaceholderText { get { return Enabled ? DisabledText : SystemColors.GrayText; } }
        public static readonly Color LinkText = Color.FromArgb(100, 170, 255);
        public static readonly Color Accent = Color.FromArgb(0, 122, 204);

        public static bool Enabled { get; private set; }

        // What a control looked like before it was darkened. Color.Empty means "use the default".
        private sealed class Original
        {
            public Color Back = Color.Empty;
            public Color Fore = Color.Empty;
            public FlatStyle Flat = FlatStyle.Standard;
            public Color FlatBorder = Color.Empty;
            public Color FlatHover = Color.Empty;
            public bool UseVisualStyleBack = true;
            public Color LinkColor = Color.Empty;
            public TabDrawMode TabDraw = TabDrawMode.Normal;
            public Image BackImage;
        }

        private static readonly ConditionalWeakTable<Control, Original> originals = new ConditionalWeakTable<Control, Original>();
        private static readonly ConditionalWeakTable<Control, object> hooked = new ConditionalWeakTable<Control, object>();

        // Switch the theme on or off for every window that is open now.
        public static void SetEnabled(bool on)
        {
            Enabled = on;

            if (on)
            {
                ToolStripManager.Renderer = new DarkRenderer();
            }
            else
            {
                ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;
            }

            List<Form> forms = new List<Form>();
            foreach (Form form in Application.OpenForms)
            {
                forms.Add(form);
            }

            foreach (Form form in forms)
            {
                ApplyTo(form);
            }
        }

        // Theme a window that was just built. In the light theme there is nothing to do; windows that
        // are open when the setting changes are handled by SetEnabled.
        public static void Apply(Control control)
        {
            if (Enabled)
            {
                ApplyTo(control);
            }
        }

        private static void ApplyTo(Control control)
        {
            if (control == null || control.IsDisposed)
            {
                return;
            }

            // Remember how everything looked before the first change: a child that is read after
            // its parent was darkened would otherwise report the parent's dark color as its own.
            Remember(control, true);
            Walk(control);
        }

        private static void Remember(Control control, bool known)
        {
            if (!originals.TryGetValue(control, out Original unused))
            {
                Original o = new Original();

                if (known)
                {
                    o.Back = control.BackColor;
                    o.Fore = control.ForeColor;

                    Button button = control as Button;
                    if (button != null)
                    {
                        o.Flat = button.FlatStyle;
                        o.FlatBorder = button.FlatAppearance.BorderColor;
                        o.FlatHover = button.FlatAppearance.MouseOverBackColor;
                        o.UseVisualStyleBack = button.UseVisualStyleBackColor;
                    }

                    ComboBox combo = control as ComboBox;
                    if (combo != null)
                    {
                        o.Flat = combo.FlatStyle;
                    }

                    TabPage page = control as TabPage;
                    if (page != null)
                    {
                        o.UseVisualStyleBack = page.UseVisualStyleBackColor;
                    }

                    LinkLabel link = control as LinkLabel;
                    if (link != null)
                    {
                        o.LinkColor = link.LinkColor;
                    }

                    TabControl tabs = control as TabControl;
                    if (tabs != null)
                    {
                        o.TabDraw = tabs.DrawMode;
                    }

                    o.BackImage = control.BackgroundImage;
                }

                originals.Add(control, o);
            }

            foreach (Control child in control.Controls)
            {
                Remember(child, known);
            }

            ToolStrip strip = control as ToolStrip;
            if (strip != null)
            {
                foreach (Control embedded in EmbeddedControls(strip.Items))
                {
                    Remember(embedded, known);
                }
            }
        }

        private static void Walk(Control control)
        {
            Hook(control);

            if (Enabled)
            {
                Darken(control);
            }
            else
            {
                Restore(control);
            }

            foreach (Control child in control.Controls)
            {
                Walk(child);
            }

            ToolStrip strip = control as ToolStrip;
            if (strip != null)
            {
                foreach (Control embedded in EmbeddedControls(strip.Items))
                {
                    Walk(embedded);
                }
            }
        }

        // Pick up controls that are added later, such as a new dock window.
        private static void Hook(Control control)
        {
            if (hooked.TryGetValue(control, out object unused))
            {
                return;
            }

            hooked.Add(control, null);
            control.ControlAdded += OnControlAdded;
        }

        private static void OnControlAdded(object sender, ControlEventArgs e)
        {
            if (!Enabled || e.Control == null || e.Control.IsDisposed)
            {
                return;
            }

            // The new control already shows its parent's colors, so its own are unknown.
            Remember(e.Control, false);
            Walk(e.Control);
        }

        // Text boxes and combo boxes that sit inside tool strips and menus.
        private static IEnumerable<Control> EmbeddedControls(ToolStripItemCollection items)
        {
            foreach (ToolStripItem item in items)
            {
                ToolStripTextBox textBox = item as ToolStripTextBox;
                if (textBox != null)
                {
                    yield return textBox.TextBox;
                }

                ToolStripComboBox comboBox = item as ToolStripComboBox;
                if (comboBox != null)
                {
                    yield return comboBox.ComboBox;
                }

                ToolStripDropDownItem dropDown = item as ToolStripDropDownItem;
                if (dropDown != null)
                {
                    foreach (Control nested in EmbeddedControls(dropDown.DropDownItems))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private static bool IsDockingLibraryControl(Control control)
        {
            // The docking library paints its own strips and captions from the dock panel's theme.
            return control.GetType().Namespace == "WeifenLuo.WinFormsUI.Docking";
        }

        private static void Darken(Control control)
        {
            Original o;
            originals.TryGetValue(control, out o);

            Form form = control as Form;
            if (form != null)
            {
                SetTitleBar(form, true);
            }

            if (IsDockingLibraryControl(control) || control is PictureBox)
            {
                return;
            }

            // Only controls on a system color are changed: anything else is a color somebody chose,
            // and then the text keeps its color too (dark text on a white label stays readable).
            bool chosenBack = o != null && !o.Back.IsEmpty && !o.Back.IsSystemColor && o.Back != Color.Transparent;
            bool changeBack = !chosenBack;
            bool changeFore = !chosenBack && (o == null || o.Fore.IsEmpty || o.Fore.IsSystemColor);

            if (control is ToolStrip)
            {
                // The renderer paints an explicit BackColor instead of its own colors, and the tool bar
                // has a light bitmap behind it.
                control.BackgroundImage = null;
                if (changeBack)
                {
                    control.BackColor = PanelBack;
                }
                if (changeFore)
                {
                    control.ForeColor = Text;
                }
                return;
            }

            if (control is Button)
            {
                if (changeBack)
                {
                    Button button = (Button)control;
                    button.FlatStyle = FlatStyle.Flat;
                    button.UseVisualStyleBackColor = false;
                    button.FlatAppearance.BorderColor = Border;
                    button.FlatAppearance.MouseOverBackColor = ButtonHover;
                    button.BackColor = ButtonBack;
                    button.ForeColor = Text;
                }
                return;
            }

            if (control is TextBoxBase || control is NumericUpDown || control is ComboBox)
            {
                if (changeBack)
                {
                    control.BackColor = InputBack;
                }
                if (changeFore)
                {
                    control.ForeColor = Text;
                }

                ComboBox combo = control as ComboBox;
                if (combo != null)
                {
                    combo.FlatStyle = FlatStyle.Flat;
                }

                TextBoxBase textBox = control as TextBoxBase;
                if (textBox != null && textBox.Multiline)
                {
                    UseDarkScrollBars(control, true);
                }
                return;
            }

            if (control is ListView || control is TreeView)
            {
                // The back color of the lists is the user's own setting (View > list color).
                if (changeFore)
                {
                    control.ForeColor = Text;
                }
                UseDarkScrollBars(control, true);
                return;
            }

            TabControl tabs = control as TabControl;
            if (tabs != null)
            {
                tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                tabs.DrawItem -= DrawTab;
                tabs.DrawItem += DrawTab;
                tabs.Invalidate();
                return;
            }

            TabPage page = control as TabPage;
            if (page != null)
            {
                page.UseVisualStyleBackColor = false;
            }

            LinkLabel link = control as LinkLabel;
            if (link != null)
            {
                link.LinkColor = LinkText;
                link.ActiveLinkColor = Color.White;
                link.VisitedLinkColor = LinkText;
            }

            if (changeBack && control.BackColor != Color.Transparent)
            {
                control.BackColor = PanelBack;
            }
            if (changeFore)
            {
                control.ForeColor = Text;
            }
            else if (chosenBack && o.Fore.IsSystemColor)
            {
                // Pin the text color: it would otherwise follow the parent to the light text color.
                control.ForeColor = o.Fore;
            }
        }

        private static void Restore(Control control)
        {
            Original o;
            if (!originals.TryGetValue(control, out o))
            {
                return;
            }

            Form form = control as Form;
            if (form != null)
            {
                SetTitleBar(form, false);
            }

            if (IsDockingLibraryControl(control) || control is PictureBox)
            {
                return;
            }

            if (control is ToolStrip)
            {
                control.BackgroundImage = o.BackImage;
            }

            Button button = control as Button;
            if (button != null)
            {
                if (o.Back.IsEmpty || o.Back.IsSystemColor)
                {
                    button.FlatStyle = o.Flat;
                    button.FlatAppearance.BorderColor = o.FlatBorder;
                    button.FlatAppearance.MouseOverBackColor = o.FlatHover;
                    button.UseVisualStyleBackColor = o.UseVisualStyleBack;
                    SetColor(control, o);
                }
                return;
            }

            ComboBox combo = control as ComboBox;
            if (combo != null)
            {
                combo.FlatStyle = o.Flat;
            }

            TabControl tabs = control as TabControl;
            if (tabs != null)
            {
                tabs.DrawItem -= DrawTab;
                tabs.DrawMode = o.TabDraw;
                tabs.Invalidate();
                return;
            }

            TabPage page = control as TabPage;
            if (page != null)
            {
                page.UseVisualStyleBackColor = o.UseVisualStyleBack;
            }

            LinkLabel link = control as LinkLabel;
            if (link != null)
            {
                link.LinkColor = o.LinkColor;
                link.ActiveLinkColor = Color.Red;
                link.VisitedLinkColor = Color.FromArgb(128, 0, 128);
            }

            if (control is TextBoxBase || control is ListView || control is TreeView)
            {
                UseDarkScrollBars(control, false);
            }

            SetColor(control, o);
        }

        // Put back the colors a control had, but only if the dark theme changed them.
        private static void SetColor(Control control, Original o)
        {
            bool changedBack = control.BackColor != Color.Transparent
                && (o.Back.IsEmpty || o.Back.IsSystemColor);
            bool changedFore = o.Fore.IsEmpty || o.Fore.IsSystemColor;

            if (changedBack && !(control is ListView) && !(control is TreeView))
            {
                if (o.Back.IsEmpty)
                {
                    control.ResetBackColor();
                }
                else
                {
                    control.BackColor = o.Back;
                }
            }

            if (changedFore)
            {
                if (o.Fore.IsEmpty)
                {
                    control.ResetForeColor();
                }
                else
                {
                    control.ForeColor = o.Fore;
                }
            }
        }

        private static void DrawTab(object sender, DrawItemEventArgs e)
        {
            TabControl tabs = (TabControl)sender;
            if (e.Index < 0 || e.Index >= tabs.TabPages.Count)
            {
                return;
            }

            bool selected = e.Index == tabs.SelectedIndex;
            Rectangle bounds = tabs.GetTabRect(e.Index);

            using (SolidBrush back = new SolidBrush(selected ? PanelBack : WindowBack))
            {
                e.Graphics.FillRectangle(back, bounds);
            }

            if (selected)
            {
                using (SolidBrush line = new SolidBrush(Accent))
                {
                    e.Graphics.FillRectangle(line, bounds.Left, bounds.Top, bounds.Width, 2);
                }
            }

            TextRenderer.DrawText(e.Graphics, tabs.TabPages[e.Index].Text, tabs.Font, bounds,
                selected ? Text : DisabledText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static void SetTitleBar(Form form, bool dark)
        {
            if (!form.TopLevel)
            {
                return;
            }

            if (!form.IsHandleCreated)
            {
                EventHandler created = null;
                created = delegate
                {
                    form.HandleCreated -= created;
                    SetTitleBar(form, Enabled);
                };
                form.HandleCreated += created;
                return;
            }

            try
            {
                int value = dark ? 1 : 0;
                // 20 is the documented attribute; 19 is what Windows 10 builds before 20H1 used.
                if (NativeMethods.DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                {
                    NativeMethods.DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static void UseDarkScrollBars(Control control, bool dark)
        {
            if (!control.IsHandleCreated)
            {
                EventHandler created = null;
                created = delegate
                {
                    control.HandleCreated -= created;
                    UseDarkScrollBars(control, Enabled);
                };
                control.HandleCreated += created;
                return;
            }

            try
            {
                NativeMethods.SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : null, null);

                if (control is ListView)
                {
                    IntPtr header = NativeMethods.SendMessage(control.Handle, NativeMethods.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                    if (header != IntPtr.Zero)
                    {
                        NativeMethods.SetWindowTheme(header, dark ? "DarkMode_ItemsView" : null, null);
                    }
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static class NativeMethods
        {
            public const int LVM_GETHEADER = 0x101F;

            [DllImport("dwmapi.dll")]
            public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

            [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
            public static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

            [DllImport("user32.dll")]
            public static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        }

        // Menus, tool bars and status bars.
        private sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors())
            {
                RoundedEdges = false;
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? Text : DisabledText;
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                e.ArrowColor = e.Item.Enabled ? Text : DisabledText;
                base.OnRenderArrow(e);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                // Draw the check mark ourselves: the stock one is black.
                Rectangle box = new Rectangle(e.ImageRectangle.Left, e.ImageRectangle.Top, e.ImageRectangle.Width, e.ImageRectangle.Height);
                using (SolidBrush back = new SolidBrush(ButtonBack))
                using (Pen border = new Pen(Accent))
                using (Pen tick = new Pen(Text, 2f))
                {
                    e.Graphics.FillRectangle(back, box);
                    e.Graphics.DrawRectangle(border, box.Left, box.Top, box.Width - 1, box.Height - 1);

                    System.Drawing.Drawing2D.SmoothingMode saved = e.Graphics.SmoothingMode;
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.DrawLines(tick, new[]
                    {
                        new Point(box.Left + 4, box.Top + box.Height / 2),
                        new Point(box.Left + box.Width / 2 - 1, box.Bottom - 5),
                        new Point(box.Right - 4, box.Top + 4)
                    });
                    e.Graphics.SmoothingMode = saved;
                }
            }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public DarkColors()
            {
                UseSystemColors = false;
            }

            public override Color ToolStripDropDownBackground { get { return PanelBack; } }
            public override Color ImageMarginGradientBegin { get { return PanelBack; } }
            public override Color ImageMarginGradientMiddle { get { return PanelBack; } }
            public override Color ImageMarginGradientEnd { get { return PanelBack; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Accent; } }
            public override Color MenuItemSelected { get { return ButtonBack; } }
            public override Color MenuItemSelectedGradientBegin { get { return ButtonBack; } }
            public override Color MenuItemSelectedGradientEnd { get { return ButtonBack; } }
            public override Color MenuItemPressedGradientBegin { get { return PanelBack; } }
            public override Color MenuItemPressedGradientMiddle { get { return PanelBack; } }
            public override Color MenuItemPressedGradientEnd { get { return PanelBack; } }
            public override Color MenuStripGradientBegin { get { return PanelBack; } }
            public override Color MenuStripGradientEnd { get { return PanelBack; } }
            public override Color ToolStripGradientBegin { get { return PanelBack; } }
            public override Color ToolStripGradientMiddle { get { return PanelBack; } }
            public override Color ToolStripGradientEnd { get { return PanelBack; } }
            public override Color ToolStripBorder { get { return Border; } }
            public override Color ToolStripContentPanelGradientBegin { get { return PanelBack; } }
            public override Color ToolStripContentPanelGradientEnd { get { return PanelBack; } }
            public override Color ToolStripPanelGradientBegin { get { return PanelBack; } }
            public override Color ToolStripPanelGradientEnd { get { return PanelBack; } }
            public override Color StatusStripGradientBegin { get { return PanelBack; } }
            public override Color StatusStripGradientEnd { get { return PanelBack; } }
            public override Color OverflowButtonGradientBegin { get { return PanelBack; } }
            public override Color OverflowButtonGradientMiddle { get { return PanelBack; } }
            public override Color OverflowButtonGradientEnd { get { return PanelBack; } }
            public override Color RaftingContainerGradientBegin { get { return PanelBack; } }
            public override Color RaftingContainerGradientEnd { get { return PanelBack; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return PanelBack; } }
            public override Color GripDark { get { return Border; } }
            public override Color GripLight { get { return PanelBack; } }
            public override Color ButtonSelectedBorder { get { return Accent; } }
            public override Color ButtonSelectedHighlight { get { return ButtonBack; } }
            public override Color ButtonSelectedGradientBegin { get { return ButtonBack; } }
            public override Color ButtonSelectedGradientMiddle { get { return ButtonBack; } }
            public override Color ButtonSelectedGradientEnd { get { return ButtonBack; } }
            public override Color ButtonPressedBorder { get { return Accent; } }
            public override Color ButtonPressedHighlight { get { return ButtonHover; } }
            public override Color ButtonPressedGradientBegin { get { return ButtonHover; } }
            public override Color ButtonPressedGradientMiddle { get { return ButtonHover; } }
            public override Color ButtonPressedGradientEnd { get { return ButtonHover; } }
            public override Color ButtonCheckedHighlight { get { return ButtonBack; } }
            public override Color ButtonCheckedGradientBegin { get { return ButtonBack; } }
            public override Color ButtonCheckedGradientMiddle { get { return ButtonBack; } }
            public override Color ButtonCheckedGradientEnd { get { return ButtonBack; } }
            public override Color CheckBackground { get { return ButtonBack; } }
            public override Color CheckSelectedBackground { get { return ButtonBack; } }
            public override Color CheckPressedBackground { get { return ButtonHover; } }
        }
    }
}
