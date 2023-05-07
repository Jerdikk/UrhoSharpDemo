using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Urho;
using Urho.Gui;
using Urho.Navigation;
using Urho.Resources;

namespace ConsoleApp4
{

    public class Tutor1 : Application
    {
        bool drawDebug;

        Node selected;
        Vector3 targetPos;

        CrowdManager crowdManager;
        UrhoConsole console;
        DebugHud debugHud;
        ResourceCache cache;
        Sprite logoSprite;
        UI ui;
        Camera camera;
        Scene scene;
        protected Node CameraNode { get; set; }
        protected float Yaw { get; set; }
        protected float Pitch { get; set; }

        static readonly Random random = new Random();
        /// Return a random float between 0.0 (inclusive) and 1.0 (exclusive.)
        public static float NextRandom() { return (float)random.NextDouble(); }
        /// Return a random float between 0.0 and range, inclusive from both ends.
        public static float NextRandom(float range) { return (float)random.NextDouble() * range; }
        /// Return a random float between min and max, inclusive from both ends.
        public static float NextRandom(float min, float max) { return (float)((random.NextDouble() * (max - min)) + min); }
        /// Return a random integer between min and max - 1.
        public static int NextRandom(int min, int max) { return random.Next(min, max); }

        public Tutor1(ApplicationOptions options = null) : base(options) { }
        static Tutor1()
        {
            Urho.Application.UnhandledException += Application_UnhandledException1;
        }

        static void Application_UnhandledException1(object sender, Urho.UnhandledExceptionEventArgs e)
        {
            if (Debugger.IsAttached && !e.Exception.Message.Contains("BlueHighway.ttf"))
                Debugger.Break();
            e.Handled = true;
        }
        protected override void Start()
        {
            base.Start();

            selected = null;

            Input.Enabled = true;

            var cache = ResourceCache;

                CreateLogo();
            SetWindowAndTitleIcon();
            CreateConsoleAndDebugHud();
            CreateUI();
            CreateScene();
            SetupViewport();
            SubscribeToEvents();
            //Input.SubscribeToKeyDown(HandleKeyDown);

        }

        void CreateScene()
        {
            var cache = ResourceCache;
            scene = new Scene();

            // Create the Octree component to the scene. This is required before adding any drawable components, or else nothing will
            // show up. The default octree volume will be from (-1000, -1000, -1000) to (1000, 1000, 1000) in world coordinates; it
            // is also legal to place objects outside the volume but their visibility can then not be checked in a hierarchically
            // optimizing manner
            scene.CreateComponent<Octree>();
            scene.CreateComponent<DebugRenderer>();


            // Create a child scene node (at world origin) and a StaticModel component into it. Set the StaticModel to show a simple
            // plane mesh with a "stone" material. Note that naming the scene nodes is optional. Scale the scene node larger
            // (100 x 100 world units)
            var planeNode = scene.CreateChild("Plane");
            planeNode.Scale = new Vector3(100, 1, 100);
            var planeObject = planeNode.CreateComponent<StaticModel>();
            planeObject.Model = cache.GetModel("Models/Plane.mdl");
            planeObject.SetMaterial(cache.GetMaterial("Materials/StoneTiled.xml"));

            // Create a directional light to the world so that we can see something. The light scene node's orientation controls the
            // light direction; we will use the SetDirection() function which calculates the orientation from a forward direction vector.
            // The light will use default settings (white light, no shadows)
            var lightNode = scene.CreateChild("DirectionalLight");
            lightNode.SetDirection(new Vector3(0.6f, -1.0f, 0.8f)); // The direction vector does not need to be normalized
            var light = lightNode.CreateComponent<Light>();
            light.LightType = LightType.Directional;

            // Create some mushrooms
            const uint numMushrooms = 100;
            for (uint i = 0; i < numMushrooms; ++i)
                CreateMushroom(new Vector3(NextRandom(90.0f) - 45.0f, 0.0f, NextRandom(90.0f) - 45.0f));
            CameraNode = scene.CreateChild("camera");
            camera = CameraNode.CreateComponent<Camera>();
            camera.FarClip = 300.0f;

            // Set an initial position for the camera scene node above the plane
            CameraNode.Position = new Vector3(0.0f, 50.0f, 0.0f);
            Pitch = 80.0f;
            CameraNode.Rotation = new Quaternion(Pitch, Yaw, 0.0f);

            // Create a DynamicNavigationMesh component to the scene root
            DynamicNavigationMesh navMesh = scene.CreateComponent<DynamicNavigationMesh>();
            // Set the agent height large enough to exclude the layers under boxes
            navMesh.AgentHeight = 10.0f;
            navMesh.CellHeight = 0.05f;
            navMesh.DrawObstacles = true;
            navMesh.DrawOffMeshConnections = true;
            // Create a Navigable component to the scene root. This tags all of the geometry in the scene as being part of the
            // navigation mesh. By default this is recursive, but the recursion could be turned off from Navigable
            scene.CreateComponent<Navigable>();
            // Add padding to the navigation mesh in Y-direction so that we can add objects on top of the tallest boxes
            // in the scene and still update the mesh correctly
            navMesh.Padding = new Vector3(0.0f, 10.0f, 0.0f);
            // Now build the navigation geometry. This will take some time. Note that the navigation mesh will prefer to use
            // physics geometry from the scene nodes, as it often is simpler, but if it can not find any (like in this example)
            // it will use renderable geometry instead
            navMesh.Build();

            // Create a CrowdManager component to the scene root
            crowdManager = scene.CreateComponent<CrowdManager>();
            var parameters = crowdManager.GetObstacleAvoidanceParams(0);
            // Set the params to "High (66)" setting
            parameters.VelBias = 0.5f;
            parameters.AdaptiveDivs = 7;
            parameters.AdaptiveRings = 3;
            parameters.AdaptiveDepth = 3;
            crowdManager.SetObstacleAvoidanceParams(0, parameters);

            // Create Jack node that will follow the path
            SpawnJack(new Vector3(-5.0f, 0.0f, 20.0f), scene.CreateChild("Jacks"));


        }

        void SetupViewport()
        {
            var renderer = Renderer;
            renderer.SetViewport(0, new Viewport(Context, scene, camera, null));
        }

        protected override void OnUpdate(float timeStep)
        {
            MoveCamera(timeStep);
            base.OnUpdate(timeStep);
        }

        bool Raycast(float maxDistance, out Vector3 hitPos, out Drawable hitDrawable)
        {
            hitDrawable = null;
            hitPos = Vector3.Zero;

            UI ui = UI;
            IntVector2 pos = ui.CursorPosition;
            // Check the cursor is visible and there is no UI element in front of the cursor
            if (!ui.Cursor.Visible || ui.GetElementAt(pos, true) != null)
                return false;

            var graphics = Graphics;
            Camera camera = CameraNode.GetComponent<Camera>();
            Ray cameraRay = camera.GetScreenRay((float)pos.X / graphics.Width, (float)pos.Y / graphics.Height);
            // Pick only geometry objects, not eg. zones or lights, only get the first (closest) hit

            var result = scene.GetComponent<Octree>().RaycastSingle(cameraRay, RayQueryLevel.Triangle, maxDistance, DrawableFlags.Geometry, uint.MaxValue);
            if (result != null)
            {
                hitPos = result.Value.Position;
                hitDrawable = result.Value.Drawable;
                return true;
            }

            return false;
        }

        bool Raycast(float maxDistance/*, out Vector3 hitPos, out Drawable hitDrawable*/)
        {
           // hitDrawable = null;
            targetPos = Vector3.Zero;

            UI ui = UI;
            IntVector2 pos = ui.CursorPosition;
            // Check the cursor is visible and there is no UI element in front of the cursor
            if (!ui.Cursor.Visible || ui.GetElementAt(pos, true) != null)
                return false;

            var graphics = Graphics;
            Camera camera = CameraNode.GetComponent<Camera>();
            Ray cameraRay = camera.GetScreenRay((float)pos.X / graphics.Width, (float)pos.Y / graphics.Height);
            // Pick only geometry objects, not eg. zones or lights, only get the first (closest) hit

            List<RayQueryResult> result = scene.GetComponent<Octree>().Raycast(cameraRay, RayQueryLevel.Triangle, maxDistance, DrawableFlags.Geometry, uint.MaxValue);
            if (result != null)
            {
                foreach (var obj in result)
                {
                    if (obj.Node.Name.Equals("Jacks"))
                    {
                        selected = obj.Node;
                        return true;
                    }
                    
                    targetPos = obj.Position;
                }
                // hitPos = result.Value.Position;
                // hitDrawable = result.Value.Drawable;
                return true;
            }

            return false;
        }

        void myPostRenderUpdate(PostRenderUpdateEventArgs e)
        {
            if (drawDebug)
            {
                // Visualize navigation mesh, obstacles and off-mesh connections
                scene.GetComponent<DynamicNavigationMesh>().DrawDebugGeometry(true);
                // Visualize agents' path and position to reach
                crowdManager.DrawDebugGeometry(true);
            }
        }

        void myCrowAgentFailure(CrowdAgentFailureEventArgs args)
        {
            Node node = args.Node;
            CrowdAgentState agentState = (CrowdAgentState)args.CrowdAgentState;

            // If the agent's state is invalid, likely from spawning on the side of a box, find a point in a larger area
            if (agentState == CrowdAgentState.StateInvalid)
            {
                // Get a point on the navmesh using more generous extents
                Vector3 newPos = scene.GetComponent<DynamicNavigationMesh>().FindNearestPoint(node.Position, new Vector3(5.0f, 5.0f, 5.0f));
                // Set the new node position, CrowdAgent component will automatically reset the state of the agent
                node.Position = newPos;
            }
        }

        void myAgentReposition(CrowdAgentRepositionEventArgs args)
        {
            string WALKING_ANI = "Models/Jack_Walk.ani";

            Node node = args.Node;
            Vector3 velocity = args.Velocity;

            // Only Jack agent has animation controller
            AnimationController animCtrl = node.GetComponent<AnimationController>();
            if (animCtrl != null)
            {
                float speed = velocity.Length;
                if (animCtrl.IsPlaying(WALKING_ANI))
                {
                    float speedRatio = speed / args.CrowdAgent.MaxSpeed;
                    // Face the direction of its velocity but moderate the turning speed based on the speed ratio as we do not have timeStep here
                    node.Rotation = Quaternion.Slerp(node.Rotation, Quaternion.FromRotationTo(Vector3.UnitZ, velocity), 10f * args.TimeStep * speedRatio);
                    // Throttle the animation speed based on agent speed ratio (ratio = 1 is full throttle)
                    animCtrl.SetSpeed(WALKING_ANI, speedRatio);
                }
                else
                    animCtrl.Play(WALKING_ANI, 0, true, 0.1f);

                // If speed is too low then stopping the animation
                if (speed < args.CrowdAgent.Radius)
                    animCtrl.Stop(WALKING_ANI, 0.8f);
            }
        }

        void SubscribeToEvents()
        {
            Engine.PostRenderUpdate += myPostRenderUpdate;

            crowdManager.CrowdAgentFailure += myCrowAgentFailure;

            crowdManager.CrowdAgentReposition += myAgentReposition;
        }

        void SetPathPoint(bool spawning)
        {
            Vector3 hitPos;
            Drawable hitDrawable;

            if (Raycast(250.0f, out hitPos, out hitDrawable))
            {

                DynamicNavigationMesh navMesh = scene.GetComponent<DynamicNavigationMesh>();
                Vector3 pathPos = navMesh.FindNearestPoint(hitPos, new Vector3(1.0f, 1.0f, 1.0f));
                Node jackGroup = scene.GetChild("Jacks", false);
                if (spawning)
                // Spawn a jack at the target position
                {
                    SpawnJack(pathPos, jackGroup);
                }
                else
                    // Set crowd agents target position
                    scene.GetComponent<CrowdManager>().SetCrowdTarget(pathPos, jackGroup);
            }
        }
        void SpawnJack(Vector3 pos, Node jackGroup)
        {
            var cache = ResourceCache;
            Node jackNode = jackGroup.CreateChild("Jack");
            jackNode.Position = pos;
            AnimatedModel modelObject = jackNode.CreateComponent<AnimatedModel>();
            modelObject.Model = (cache.GetModel("Models/Jack.mdl"));
            modelObject.SetMaterial(cache.GetMaterial("Materials/Jack.xml"));
            modelObject.CastShadows = true;
            jackNode.CreateComponent<AnimationController>();

            // Create the CrowdAgent
            var agent = jackNode.CreateComponent<CrowdAgent>();
            agent.Height = 2.0f;
            agent.MaxSpeed = 3.0f;
            agent.MaxAccel = 3.0f;
        }
        void AddOrRemoveObject()
        {
            // Raycast and check if we hit a mushroom node. If yes, remove it, if no, create a new one
            Vector3 hitPos;
            Drawable hitDrawable;
            
            if (Raycast(250.0f, out hitPos, out hitDrawable))
            {
                Node hitNode = hitDrawable.Node;

                // Note that navmesh rebuild happens when the Obstacle component is removed
                if (hitNode.Name == "Mushroom")
                    hitNode.Remove();
                else if (hitNode.Name == "Jack")
                    hitNode.Remove();
                else
                    CreateMushroom(hitPos);
            }
        }
        void CreateMushroom(Vector3 pos)
        {
            var cache = ResourceCache;

            Node mushroomNode = scene.CreateChild("Mushroom");
            mushroomNode.Position = (pos);
            mushroomNode.Rotation = new Quaternion(0.0f, NextRandom(360.0f), 0.0f);
            mushroomNode.SetScale(2.0f + NextRandom(0.5f));
            StaticModel mushroomObject = mushroomNode.CreateComponent<StaticModel>();
            mushroomObject.Model = (cache.GetModel("Models/Mushroom.mdl"));
            mushroomObject.SetMaterial(cache.GetMaterial("Materials/Mushroom.xml"));
            mushroomObject.CastShadows = true;
            // Create the navigation obstacle
            Obstacle obstacle = mushroomNode.CreateComponent<Obstacle>();
            obstacle.Radius = mushroomNode.Scale.X;
            obstacle.Height = mushroomNode.Scale.Y;
        }
        protected void MoveCamera(float timeStep)
        {
            // Right mouse button controls mouse cursor visibility: hide when pressed
            UI ui = UI;
            Input input = Input;
            ui.Cursor.Visible = !input.GetMouseButtonDown(MouseButton.Right);

            // Do not move if the UI has a focused element (the console)
            if (ui.FocusElement != null)
                return;

            // Movement speed as world units per second
            const float moveSpeed = 20.0f;
            // Mouse sensitivity as degrees per pixel
            const float mouseSensitivity = 0.1f;

            // Use this frame's mouse motion to adjust camera node yaw and pitch. Clamp the pitch between -90 and 90 degrees
            // Only move the camera when the cursor is hidden
            if (!ui.Cursor.Visible)
            {
                IntVector2 mouseMove = input.MouseMove;
                Yaw += mouseSensitivity * mouseMove.X;
                Pitch += mouseSensitivity * mouseMove.Y;
                Pitch = MathHelper.Clamp(Pitch, -90.0f, 90.0f);

                // Construct new orientation for the camera scene node from yaw and pitch. Roll is fixed to zero
                CameraNode.Rotation = new Quaternion(Pitch, Yaw, 0.0f);
            }

            // Read WASD keys and move the camera scene node to the corresponding direction if they are pressed
            if (input.GetKeyDown(Key.W)) CameraNode.Translate(Vector3.UnitZ * moveSpeed * timeStep);
            if (input.GetKeyDown(Key.S)) CameraNode.Translate(-Vector3.UnitZ * moveSpeed * timeStep);
            if (input.GetKeyDown(Key.A)) CameraNode.Translate(-Vector3.UnitX * moveSpeed * timeStep);
            if (input.GetKeyDown(Key.D)) CameraNode.Translate(Vector3.UnitX * moveSpeed * timeStep);

            const int qualShift = 1;

            // Set destination or spawn a new jack with left mouse button
            if (input.GetMouseButtonPress(MouseButton.Left))
                SetPathPoint(input.GetQualifierDown(qualShift));
            // Add or remove objects with middle mouse button, then rebuild navigation mesh partially
            if (input.GetMouseButtonPress(MouseButton.Middle))
                AddOrRemoveObject();

            // Check for loading/saving the scene. Save the scene to the file Data/Scenes/CrowdNavigation.xml relative to the executable
            // directory
            if (input.GetKeyPress(Key.F5))
                scene.SaveXml(FileSystem.ProgramDir + "Data/Scenes/CrowdNavigation.xml");

            if (input.GetKeyPress(Key.F7))
                scene.LoadXml(FileSystem.ProgramDir + "Data/Scenes/CrowdNavigation.xml");

            // Toggle debug geometry with space
            if (input.GetKeyPress(Key.Space))
                drawDebug = !drawDebug;
        }

        void HandleKeyDown(KeyDownEventArgs e)
        {
            if (e.Key == Key.Esc)
            {
                Exit(); 
                return;
            }
        }

        protected void CreateUI(string extra = "")
        {
            var cache = ResourceCache;

            // Create a Cursor UI element because we want to be able to hide and show it at will. When hidden, the mouse cursor will
            // control the camera, and when visible, it will point the raycast target
            XmlFile style = cache.GetXmlFile("UI/DefaultStyle.xml");
            Cursor cursor = new Cursor();
            cursor.SetStyleAuto(style);
            UI.Cursor = cursor;

            // Set starting position of the cursor at the rendering window center
            var graphics = Graphics;
            cursor.SetPosition(graphics.Width / 2, graphics.Height / 2);

            // Construct new Text object, set string to display and font to use
            var instructionText = new Text();

            instructionText.Value =
                "Use WASD keys to move, RMB to rotate view\n" +
                "LMB to set destination, SHIFT+LMB to spawn a Jack\n" +
                "CTRL+LMB to teleport main agent\n" +
                "MMB to add obstacles or remove obstacles/agents\n" +
                "F5 to save scene, F7 to load\n" +
                "Space to toggle debug geometry";

            instructionText.SetFont(cache.GetFont("Fonts/Anonymous Pro.ttf"), 15);
            // The text has multiple rows. Center them in relation to each other
            instructionText.TextAlignment = HorizontalAlignment.Center;

            // Position the text relative to the screen center
            instructionText.HorizontalAlignment = HorizontalAlignment.Center;
            instructionText.VerticalAlignment = VerticalAlignment.Center;
            instructionText.SetPosition(0, UI.Root.Height / 4);
            UI.Root.AddChild(instructionText);
        }

       
        void CreateLogo()
        {
            cache = ResourceCache;
            var logoTexture = cache.GetTexture2D("Textures/LogoLarge.png");

            if (logoTexture == null)
                return;

            ui = UI;
            logoSprite = ui.Root.CreateSprite();
            logoSprite.Texture = logoTexture;
            int w = logoTexture.Width;
            int h = logoTexture.Height;
            logoSprite.SetScale(256.0f / w);
            logoSprite.SetSize(w, h);
            logoSprite.SetHotSpot(0, h);
            logoSprite.SetAlignment(HorizontalAlignment.Left, VerticalAlignment.Bottom);
            logoSprite.Opacity = 0.75f;
            logoSprite.Priority = -100;
        }

        void SetWindowAndTitleIcon()
        {
            var icon = cache.GetImage("Textures/UrhoIcon.png");
            Graphics.SetWindowIcon(icon);
            Graphics.WindowTitle = "UrhoSharp Sample";
        }

        void CreateConsoleAndDebugHud()
        {
            var xml = cache.GetXmlFile("UI/DefaultStyle.xml");
            console = Engine.CreateConsole();
            console.DefaultStyle = xml;
            console.Background.Opacity = 0.8f;

            debugHud = Engine.CreateDebugHud();
            debugHud.DefaultStyle = xml;
        }


    }

    class Program
    {
        static void Main(string[] args)
        {
            string tt = Directory.GetCurrentDirectory();
            ApplicationOptions ap = new ApplicationOptions("Data");
            Tutor1 hw = new Tutor1(ap);
            try
            {
                hw.Run();
            }
            catch
            {

            }
        }
    }

}
