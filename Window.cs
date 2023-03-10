using OpenTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL4;
using System.Drawing;
using OpenTK.Input;
using System.Runtime.InteropServices;

using BuildPlate_Editor.Maths;
using System.Diagnostics;
using System.IO;

using Font = BuildPlate_Editor.Font;
using BuildPlate_Editor.UI;
using System.Threading;
using SystemPlus;

namespace BuildPlate_Editor
{
    public class Window : GameWindow
    {
        public float AspectRatio;

        Shader shader;
        Shader shader2;
        Shader colShader;
        Shader skyboxShader;
        Shader uiShader;

        bool worldLoaded = false;

        public static BlockOutline outline;

        // Mouse
        bool mouseLocked;
        Point lastMousePos;
        Point origCursorPosition; // position before lock

        public Font font;
        public KeyboardState keyboardState;
        public Vector2 mousePos;

        public Window()
        {
            Width = 1280;
            Height = 720;
            AspectRatio = (float)Width / (float)Height;
            Title = "BuildPlate_Editor";
        }

        public DebugProc debMessageCallback;

        protected override void OnLoad(EventArgs e)
        {
            MakeCurrent();
            GL.Enable(EnableCap.DebugOutput);
            debMessageCallback = new DebugProc(MessageCallback); // Fixed error: A callback was made on a garbage collected delegate
            GL.DebugMessageCallback(debMessageCallback, IntPtr.Zero);

            // load shaders
            shader = new Shader(); // chunk shader
            shader.Compile("shader");
            shader2 = new Shader(); // not used by anything now
            shader2.Compile("shader2");
            colShader = new Shader(); // used by block outline
            colShader.Compile("colShader");
            skyboxShader = new Shader();
            skyboxShader.Compile("skybox");
            uiShader = new Shader(); // Used by UI
            uiShader.Compile("ui");

            BlockToPlace.Init();

            // all UI stuff copyed from my minecraft repo
            // load font
            font = new Font("Minecraft", 32);
            GUI.Init(font);
            GUI.SetScene(0); // loading

#if DEBUG
            World.Init();
#else
                try {
                    World.Init();
                }
                catch (Exception ex) {
                    Util.Exit(EXITCODE.World_Unknown, ex);
                }
#endif

            Thread worldLoadThread = new Thread(() =>
            {
                World.InitChunks();
                worldLoaded = true;
            });
            worldLoadThread.Start();

            SkyBox.Init("Cold_Sunset", Camera.position, 100f);

            outline = new BlockOutline(1.1f, 4f, 3.0f, Color.Red);
            
            base.WindowBorder = WindowBorder.Fixed;
            base.WindowState = WindowState.Normal;
            GL.Viewport(0, 0, Width, Height);

            GL.Enable(EnableCap.DepthTest);
            GL.Enable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Bind();
            Camera.SetRotation(new Vector3(0f, 180f, 0f));
            Camera.UpdateView(Width, Height);
            shader.UploadMat4("uProjection", ref Camera.projMatrix);
            shader.UploadMat4("uView", ref Camera.viewMatrix);

            Icon = Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);

            LockMouse();
        }

        private void MessageCallback(DebugSource source, DebugType type, int id, DebugSeverity severity, int length, IntPtr message, IntPtr userParam)
        {
            if (id == 131185)
                return;
            byte[] managedArray = new byte[length];
            Marshal.Copy(message, managedArray, 0, length);
            Console.WriteLine($"MessageCallback: Source:{source}, Type:{type}, id:{id}, " +
                $"Severity:{severity}, Message: {Encoding.ASCII.GetString(managedArray)}");
        }

        protected override void OnUpdateFrame(FrameEventArgs e)
        {
            float delta = (float)e.Time;

            if (GUI.Scene == 1) {
                // Rotation
                if (keyboardState.IsKeyDown(Key.Left))
                    Camera.Rotation.Y += delta * 160f;
                else if (keyboardState.IsKeyDown(Key.Right))
                    Camera.Rotation.Y -= delta * 160f;
                if (keyboardState.IsKeyDown(Key.Up))
                    Camera.Rotation.X -= delta * 80f;
                else if (keyboardState.IsKeyDown(Key.Down))
                    Camera.Rotation.X += delta * 80f;
            }

            if (mouseLocked) {
                var mouseDelta = System.Windows.Forms.Cursor.Position - new Size(lastMousePos);
                if (mouseDelta != Point.Empty) {
                    Camera.Rotation.X += mouseDelta.Y * 0.25f;
                    Camera.Rotation.Y += -mouseDelta.X * 0.25f;
                    CenterCursor();
                }
            }


            if (Camera.Rotation.X < -85)
                Camera.Rotation.X = -85;
            else if (Camera.Rotation.X > 85)
                Camera.Rotation.X = 85;

            // Movement
            if (GUI.Scene == 1) {
                if (keyboardState.IsKeyDown(Key.W))
                    Camera.Move(0f, delta * 8f);
                else if (keyboardState.IsKeyDown(Key.S))
                    Camera.Move(180f, delta * 8f);
                if (keyboardState.IsKeyDown(Key.A))
                    Camera.Move(90f, delta * 8f);
                else if (keyboardState.IsKeyDown(Key.D))
                    Camera.Move(270f, delta * 8f);
                if (keyboardState.IsKeyDown(Key.Space))
                    Camera.position.Y += delta * 6f;
                else if (keyboardState.IsKeyDown(Key.ShiftLeft))
                    Camera.position.Y -= delta * 6f;

                Camera.UpdateView(Width, Height);
            }

            if (GUI.Scene == 1 && wantToSave) {
                GUI.SetScene(2);
                UnlockMouse();
                Thread saveThread = new Thread(() =>
                {
                    System.Windows.Forms.DialogResult res = System.Windows.Forms.MessageBox.Show("Are you sure you want to save", "Confirm save",
                        System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question,
                        System.Windows.Forms.MessageBoxDefaultButton.Button1);
                    if (res == System.Windows.Forms.DialogResult.Yes)
                        World.Save();

                    wantToSave = false;
                    GUI.SetScene(1);
                });
                saveThread.Start();
            }

            Console.Title = Camera.position.ToString();

            // Other keyboard
            if (keyboardState.IsKeyDown(Key.Escape))
                UnlockMouse();

            //if (!outline.Raycast)
                outline.Update();

            GUI.Update(delta);

            float FPS = 1f / delta;
            Title = $"BuildPlate_Editor FPS: {MathPlus.Round(FPS, 2)}";
        }

        protected override void OnRenderFrame(FrameEventArgs e)
        {
            GL.ClearColor(Color.FromArgb(92, 157, 255));
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            colShader.Bind();
            colShader.UploadMat4("uProjection", ref Camera.projMatrix);
            colShader.UploadMat4("uView", ref Camera.viewMatrix);

            shader.Bind();
            shader.UploadMat4("uProjection", ref Camera.projMatrix);
            shader.UploadMat4("uView", ref Camera.viewMatrix);
            if (GUI.Scene > 0)
                World.Render(shader, colShader);

            colShader.Bind();
            if (GUI.Scene > 0)
                outline.Render(colShader);

            skyboxShader.Bind();
            skyboxShader.UploadMat4("uProjection", ref Camera.projMatrix);
            skyboxShader.UploadMat4("uView", ref Camera.viewMatrix);
            GL.Disable(EnableCap.CullFace);
            SkyBox.pos = Camera.position;
            if (GUI.Scene > 0)
                SkyBox.Render(skyboxShader);
            GL.Enable(EnableCap.CullFace);

            GL.DepthMask(false);
            GUI.Render(uiShader);
            GL.DepthMask(true);

            SwapBuffers();

            LateUpdate();
        }

        private void LateUpdate()
        {
            if (wantToTakeInput) {
                string input = BlockToPlace.TakeInput();
                if (input != string.Empty) {
                    if (!World.WillCreateValidTextures(input, 0)) {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"\"{input}\" isn't valid block");
                        Console.ResetColor();
                    }
                    else
                        World.BlockToPlace = input;
                }
                GUI.SetScene(0);
                wantToTakeInput = false;
            }
        }

        bool wantToSave = false;
        bool wantToTakeInput = false;
        protected override void OnKeyDown(KeyboardKeyEventArgs e)
        {
            if (e.Key == Key.P) {
                Vector3i pos = (Vector3i)outline.Position;
                World.GetBlockIndex(pos, out int sbi, out int bi);
                if (sbi != -1 && bi != -1)
                    Console.WriteLine($"Palette Id: {World.GetBlock(sbi, bi)}, Texture Id: {World.GetBlockPalette(sbi, bi).textures[0]}," +
                                    $"Name: {World.GetBlockPalette(sbi, bi).name}, Data: {World.GetBlockPalette(sbi, bi).data}, Chunk Index: {sbi}, Block Index: {bi}");
                else
                    Console.WriteLine("Couldn't get block index or sub chunk");
            }
            else if (e.Key == Key.E) {
                GUI.SetScene(3);
                wantToTakeInput = true;
            }
            else if (e.Key == Key.S && (e.Modifiers & KeyModifiers.Control) == KeyModifiers.Control) // save
                wantToSave = true;
            else if (e.Key == Key.C)
                World.ShowChunkOutlines = !World.ShowChunkOutlines;
            else if (e.Key == Key.M)
                outline.Raycast = !outline.Raycast;
            else
                keyboardState = e.Keyboard;

            GUI.OnKeyDown(e.Key, e.Modifiers);
        }
        protected override void OnKeyUp(KeyboardKeyEventArgs e)
        {
            keyboardState = e.Keyboard;
            GUI.OnKeyUp(e.Key, e.Modifiers);
        }
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            GUI.OnKeyPress(e.KeyChar);
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            LockMouse();
            GUI.OnMouseDown(e.Button, e.Position);

            if (GUI.Scene == 1) {
                if (outline.Raycast)
                    outline.Update();
                if (e.Button == MouseButton.Left)
                    World.SetBlock((Vector3i)outline.raycastResult.HitPos, "air");
                if (e.Button == MouseButton.Right)
                    World.SetBlock((Vector3i)outline.raycastResult.LastPos, World.BlockToPlace);
            }
        }
        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            GUI.OnMouseUp(e.Button, e.Position);
        }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            const float speed = 1.0f;
            if (e.Delta > 0) {
                outline.blocksFromCam += speed;
            } else if (e.Delta < 0) {
                outline.blocksFromCam -= speed;
            }

            outline.blocksFromCam = MathPlus.Clamp(outline.blocksFromCam, 1f, 20f);
        }

        // Mouse
        private void CenterCursor()
        {
            System.Windows.Forms.Cursor.Position = new Point(Width / 2 + Location.X, Height / 2 + Location.Y);
            lastMousePos = System.Windows.Forms.Cursor.Position;
        }
        protected void LockMouse()
        {
            mouseLocked = true;
            origCursorPosition = System.Windows.Forms.Cursor.Position;
            CursorVisible = false;
            CenterCursor();
        }

        protected void UnlockMouse()
        {
            mouseLocked = false;
            CursorVisible = true;
            System.Windows.Forms.Cursor.Position = origCursorPosition;
        }
    }
}
