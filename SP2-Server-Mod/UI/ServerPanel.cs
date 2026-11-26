using System;
using System.Xml.Linq;
using Assets.Scripts.Flight.UI.Panels;
using Jundroo.Juicy.Widgets;

namespace SP2ServerMod.UI
{
    // Opens and closes the mod's menu, adds its button to the flight menu and provides the menu's layout.
    internal static class ServerPanel
    {
        // The path the game "loads" for the panel. UiPatches hands out our own XML for it.
        public const string PanelXmlPath = "Mods/SP2ServerMod/Panel";
        public const string ButtonId = "sp2-server-mod-menu-button";

        private const string ButtonColor = "#3A8FE8";
        private const string ButtonColorActive = "#1F63B0";

        private static ServerPanelScript panel;
        private static ButtonWidget menuButton;
        private static float attachAt = -1f;

        public static bool IsOpen
        {
            get { return panel != null && !panel.IsClosed; }
        }

        // Called each time the flight menu is built.
        public static void AddMenuButton(Widget menu)
        {
            if (menu.FindWidget(ButtonId) != null) return;

            XElement element = new XElement("Button",
                new XAttribute("id", ButtonId),
                new XAttribute("class", "btn btn-default menu-tooltip"),
                new XAttribute("color", ButtonColor),
                new XAttribute("tooltip", "Matches and their spawns: positions and vertical beams to find them."),
                new XElement("Text", new XAttribute("text", "SP2 Server Mod")));

            ButtonWidget button = (ButtonWidget)menu.Context.CreateWidget(element, menu, menu.Stylesheet);
            button.Clicked += (Widget w) => Toggle();

            menuButton = button;
            panel = null;   // a new flight scene means any old panel is gone
            UpdateButtonColor();
        }

        // A copy of the plugin loaded while a flight is on (a reload with F6) missed the moment the game built the flight menu, so it adds
        // its button to the menu that is already there. Called once, a moment after loading, so the copy it replaces has taken its own
        // button out first.
        public static void Tick()
        {
            if (attachAt < 0f) attachAt = UnityEngine.Time.unscaledTime + 1.5f;
            if (attachAt == 0f || UnityEngine.Time.unscaledTime < attachAt) return;
            attachAt = 0f;
            AttachToExistingMenus();
        }

        private static void AttachToExistingMenus()
        {
            foreach (MenuPanelScript script in UnityEngine.Object.FindObjectsByType<MenuPanelScript>(
                         UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
            {
                Widget widget = script != null ? script.Widget : null;
                if (widget != null) AddMenuButton(widget);
            }
        }

        // The plugin is being unloaded: the panel is closed and the button leaves the flight menu.
        public static void Shutdown()
        {
            ButtonWidget button = menuButton;
            menuButton = null;   // so that closing the panel does not try to paint the button

            ServerPanelScript open = panel;
            panel = null;
            if (open != null && !open.IsClosed) open.Close();

            if (button != null) button.Destroy();
        }

        public static void Toggle()
        {
            if (IsOpen)
            {
                panel.Close();
                return;
            }

            try
            {
                panel = Assets.Scripts.Game.Instance.UserInterface.CreateDialog<ServerPanelScript>(PanelXmlPath);
                panel.Closed += (Assets.Scripts.UI.IDialog d) =>
                {
                    panel = null;
                    UpdateButtonColor();
                };
            }
            catch (Exception e)
            {
                SP2ServerModPlugin.Log.LogError("Couldn't open the SP2 Server Mod menu: " + e);
                panel = null;
            }
            UpdateButtonColor();
        }

        private static void UpdateButtonColor()
        {
            if (menuButton != null)
                menuButton.SetStyle("color", IsOpen ? ButtonColorActive : ButtonColor);
        }

        // The menu's layout (the same kind of window as the Armor Mod's, on the other side of the screen so both can be open). Single quotes keep
        // the C# string readable.
        public static XElement BuildLayout()
        {
            string xml = @"
<Xml>
   <Stylesheets>
      <Stylesheet path='Xml/Styles' />
      <Stylesheet>
         <Styles>
            <Style class='sp2sm-dialog' width='560' maxHeightPercent='90' />
         </Styles>
      </Stylesheet>
   </Stylesheets>
   <Widgets>
      <Instance id='sp2sm-dialog' templateId='dialog' class='sp2sm-dialog' contentPadding='7 7 7 7'
                anchor='TopRight' position='-25 -25' script='{SCRIPT}' title='SP2 Server Mod'
                backerVisible='false' buttonRowVisible='false' spacerVisible='false'>
         <Section sectionId='title-buttons'>
            <Button id='sp2sm-close-button' class='btn btn-danger' width='35' height='35' anchor='MiddleRight'>
               <Image sprite='Sprites/Common/IconButtonClose' />
            </Button>
         </Section>
         <Section sectionId='content'>
            <ScrollView fitContentHeight='true' verticalScrollbarVisibility='AutoHideAndExpandViewport'>
               <VerticalLayout id='sp2sm-content' class='vertical-layout'
                               padding='@HalfSpacing @Spacing @HalfSpacing @HalfSpacing' spacing='@Spacing' />
            </ScrollView>
         </Section>
      </Instance>
   </Widgets>
</Xml>";
            return XElement.Parse(xml.Replace("{SCRIPT}", typeof(ServerPanelScript).FullName));
        }
    }
}
