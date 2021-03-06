﻿using System;
using System.Collections.Generic;
using System.Text;
using TgcViewer.Example;
using TgcViewer;
using Microsoft.DirectX.Direct3D;
using System.Drawing;
using Microsoft.DirectX;
using TgcViewer.Utils.Modifiers;
using TgcViewer.Utils.TgcSceneLoader;
using TgcViewer.Utils.TgcGeometry;
using TgcViewer.Utils._2D;
using TgcViewer.Utils.Terrain;
using TgcViewer.Utils;

namespace Examples.GpuOcclusion.Viejo
{
    /// <summary>
    /// Demo GPU occlusion Culling
    /// GIGC - UTN-FRBA
    /// </summary>
    public class DemoGPUCulling : TgcExample
    {

        #region Members

        //The maximum number of total occludees in scene.
        //TODO: Mati, mete codigo aca.
        const int MAX_OCCLUDEES = 4096;
        const float TextureSize = 64;
        const bool enableZPyramid = true;

        //The hierarchical Z-Buffer (HiZ) texture.
        //Sepparated as even and odd mip levels. See Nick Darnells' blog.
        // 0 is even 1 is odd.
        Texture[] HiZBufferTex;

        //The hierarchical Z-Buffer mipmap chains.
       
         //The number of mip levels for the Hi Z texture;
        int mipLevels;

        //The results of the occlusion test texture;
        Texture OcclusionResultTex;

 
        //The surface to store the results of the occlusion test.
        Surface OcclusionResultSurface;

        //The effect to render the Hi Z buffer.
        Effect OcclusionEffect;

        Device d3dDevice;

        //The mesh to draw as example.
        Mesh teapot;

        //The textures to store the Occludees AABB and Depth.
        Texture OccludeeDataTextureAABB, OccludeeDataTextureDepth;

        //The vertices that form the quad needed to execute the occlusion test pixel shaders.
        CustomVertex.TransformedTextured[] ScreenQuadVertices;

        Surface pOldRT;
        VertexFormats oldVertexFormat;

        bool OcclusionWithPyramid = true;

        Random rnd = new Random();

        #endregion

        public override string getCategory()
        {
            return "Viejo";
        }

        public override string getName()
        {
            return "Lea - GPU Culling";
        }

        public override string getDescription()
        {
            return "Lea - GPU Culling";
        }

        public override void init()
        {
            d3dDevice = GuiController.Instance.D3dDevice;

            GuiController.Instance.CustomRenderEnabled = true;

            GuiController.Instance.Modifiers.addBoolean("UsePyramid", "UsePyramid", OcclusionWithPyramid);

            float aspectRatio = (float)GuiController.Instance.Panel3d.Width / GuiController.Instance.Panel3d.Height;
            d3dDevice.Transform.Projection = Matrix.PerspectiveFovLH(TgcD3dDevice.fieldOfViewY, aspectRatio, TgcD3dDevice.zNearPlaneDistance, TgcD3dDevice.zFarPlaneDistance);


            GuiController.Instance.FpsCamera.Enable = true;
            GuiController.Instance.FpsCamera.setCamera(new Vector3(0, 0, -10), new Vector3(0, 0, 0));

            int mipValue;

            if (OcclusionWithPyramid)
                mipValue = 0; //Creates all the mip levels needed.
            else
                mipValue = 1; //Sticks to only one mip level.


            HiZBufferTex = new Texture[2];
            //Create the Occlusion map (Hierarchical Z Buffer).
            //Format.R32F

            for (int i = 0; i < 2; i++)
            {
                HiZBufferTex[i] = new Texture(d3dDevice, GuiController.Instance.D3dDevice.Viewport.Width,
                                             GuiController.Instance.D3dDevice.Viewport.Height, mipValue, Usage.RenderTarget,
                                             Format.R32F, Pool.Default);
            }
 

            //Get the number of mipmap levels.
            mipLevels = HiZBufferTex[0].LevelCount;



            //Create the texture that will hold the results of the occlusion test.
            OcclusionResultTex = new Texture(d3dDevice, (int)TextureSize, (int)TextureSize, 1, Usage.RenderTarget, Format.R16F, Pool.Default);

            //Get the surface.
            OcclusionResultSurface = OcclusionResultTex.GetSurfaceLevel(0);


            string MyShaderDir = GuiController.Instance.ExamplesDir + "media\\Shaders\\Viejo\\";

            //Load the Shader
            string compilationErrors;
            //OcclusionEffect = Effect.FromFile(d3dDevice, MyShaderDir + "OcclusionMap.fx", null, null, ShaderFlags.None, null, out compilationErrors);

            OcclusionEffect = Effect.FromFile(d3dDevice, MyShaderDir + "OcclusionMap.fxo", null, null, ShaderFlags.NotCloneable, null, out compilationErrors);
            if (OcclusionEffect == null)
            {
                throw new Exception("Error al cargar shader. Errores: " + compilationErrors);
            }

            teapot = Mesh.Teapot(d3dDevice);


            //Create the vertex buffer with occludees.
            createOccludees();
        }


        public override void render(float elapsedTime)
        {
          //  if( GuiController.Instance.D3dInput.keyPressed(Microsoft.DirectX.DirectInput.Key.P) )

            OcclusionWithPyramid = (bool)GuiController.Instance.Modifiers["UsePyramid"] ;


            DrawOcclusionBuffer();

            
            
        }

        private void DrawSprite(Texture tex, Point pos, float scale)
        {
            DrawSprite(tex, pos, scale, 0);
        }

        private void DrawSprite(Texture tex, Point pos, float scale, int mipMapLevel)
        {
            using (Sprite spriteobject = new Sprite(d3dDevice))
            {
                spriteobject.Begin(SpriteFlags.DoNotModifyRenderState);
                spriteobject.Transform = Matrix.Scaling(scale, scale, scale);
                spriteobject.Draw(tex, new Rectangle(0, 0, tex.GetSurfaceLevel(mipMapLevel).Description.Width, tex.GetSurfaceLevel(mipMapLevel).Description.Height), new Vector3(0, 0, 0), new Vector3(pos.X, pos.Y, 0), Color.White);
                spriteobject.End();
            }
        }
        private void DrawOcclusionBuffer()
        {


            //Draw the low detail occluders. Generate the Hi Z buffer
            DrawOccluders();

            //Perform the occlusion culling test. Obtain the visible set.
            PerformOcclussionCulling();

            //Draw the visible set.
            DrawGeometryWithOcclusionEnabled();

            //Show the occlusion related textures for debugging.
            DebugTexturesToScreen();

        }

        //Renders the debug textures.
        private void DebugTexturesToScreen()
        {
            d3dDevice.BeginScene();

            d3dDevice.SetRenderState(RenderStates.ZEnable, false);
            d3dDevice.SetRenderState(RenderStates.ZBufferWriteEnable, false);

            //Set screen as render target.
            d3dDevice.SetRenderTarget(0, pOldRT);


            d3dDevice.VertexFormat = oldVertexFormat;
            
            //Transformed vertices don't need vertex shader execution.
            CustomVertex.TransformedTextured[] MipMapQuadVertices = new CustomVertex.TransformedTextured[4];

            int originalMipWidth = HiZBufferTex[0].GetSurfaceLevel(0).Description.Width;
            int originalMipHeight = HiZBufferTex[0].GetSurfaceLevel(0).Description.Height;

            int posXMipMap = 0;

            for (int i = 1; i < mipLevels; i++)
            {

                OcclusionEffect.SetValue("mipLevel", i); 
                OcclusionEffect.Technique = "DebugSpritesMipLevel";

                if ( i > 0 )
                    OcclusionEffect.SetValue("LastMip", HiZBufferTex[(i) % 2]);
                else
                    OcclusionEffect.SetValue("LastMip", HiZBufferTex[0]); //CHANGE THIS with 0
                

                d3dDevice.VertexFormat = CustomVertex.TransformedTextured.Format;

                //Get the mip map level dimensions.
                int mipWidth = originalMipWidth >> (i);
                int mipHeight = originalMipHeight >> (i);
                
                //Create a screenspace quad for the position and size of the mip map.
                UpdateMipMapVertices(ref MipMapQuadVertices, posXMipMap, 0, mipWidth, mipHeight);
                
                int numPasses = OcclusionEffect.Begin(0);
                for (int n = 0; n < numPasses; n++)
                {
                    OcclusionEffect.BeginPass(n);

                    d3dDevice.DrawUserPrimitives(PrimitiveType.TriangleFan, 2, MipMapQuadVertices);
                    OcclusionEffect.EndPass();
                }
                OcclusionEffect.End();
                posXMipMap += mipWidth + 10;

            }

            DrawSprite(OcclusionResultTex, new Point(20, 250), 2.0f);
            DrawSprite(OccludeeDataTextureAABB, new Point(20, 100), 2.0f);
            DrawSprite(OccludeeDataTextureDepth, new Point(20, 175), 2.0f);



            //Surface offScreenSurface;

            //offScreenSurface = d3dDevice.CreateOffscreenPlainSurface(OcclusionResultSurface.Description.Width, OcclusionResultSurface.Description.Height, OcclusionResultSurface.Description.Format, Pool.SystemMemory);
            //d3dDevice.GetRenderTargetData(OcclusionResultSurface, offScreenSurface);

            //GraphicsStream stream = offScreenSurface.LockRectangle(LockFlags.ReadOnly);

            //int texCount = OcclusionResultSurface.Description.Width * OcclusionResultSurface.Description.Height;
            //float[] values = new float[texCount];

            //values = (float[])stream.Read(typeof(float), 0, texCount);
            //offScreenSurface.UnlockRectangle();

            d3dDevice.EndScene();


            
        }

        private void UpdateMipMapVertices(ref CustomVertex.TransformedTextured[] MipMapQuadVertices, int x, int y, int mipWidth, int mipHeight)
        {

            const float texelOffset = 0.5f;

            MipMapQuadVertices[0].Position = new Vector4(x - texelOffset, y - texelOffset, 0f, 1f);
            MipMapQuadVertices[0].Rhw = 1.0f;
            MipMapQuadVertices[0].Tu = 0.0f;
            MipMapQuadVertices[0].Tv = 0.0f;

            MipMapQuadVertices[1].Position = new Vector4(x + mipWidth - texelOffset, y - texelOffset, 0f, 1f);
            MipMapQuadVertices[1].Rhw = 1.0f;
            MipMapQuadVertices[1].Tu = 1.0f;
            MipMapQuadVertices[1].Tv = 0.0f;


            MipMapQuadVertices[2].Position = new Vector4(x + mipWidth - texelOffset, y + mipHeight - texelOffset, 0f, 1f);
            MipMapQuadVertices[2].Rhw = 1.0f;
            MipMapQuadVertices[2].Tu = 1.0f;
            MipMapQuadVertices[2].Tv = 1.0f;

            MipMapQuadVertices[3].Position = new Vector4(x - texelOffset, y + mipHeight - texelOffset, 0f, 1f);
            MipMapQuadVertices[3].Rhw = 1.0f;
            MipMapQuadVertices[3].Tu = 0.0f;
            MipMapQuadVertices[3].Tv = 1.0f;

        }

        private void DrawOccluders()
        {
            d3dDevice.BeginScene();

            //Store the original render target.
            pOldRT = d3dDevice.GetRenderTarget(0);

            //Get the Hierarchical zBuffer surface at mip level 0.
            Surface pHiZBufferSurface = HiZBufferTex[0].GetSurfaceLevel(0);

            //Set the render target.
            d3dDevice.SetRenderTarget(0, pHiZBufferSurface);

            d3dDevice.Clear(ClearFlags.Target | ClearFlags.ZBuffer, Color.Black, 1.0f, 0);

            //Enable Z test and Z write.
            d3dDevice.SetRenderState(RenderStates.ZEnable, true);
            d3dDevice.SetRenderState(RenderStates.ZBufferWriteEnable, true);

            //Draw the objects being occluded
            DrawTeapots(true, "HiZBuffer");

            d3dDevice.EndScene();

            BuildMipMapChain();

            d3dDevice.SetRenderTarget(0, pOldRT);
        }

        private void BuildMipMapChain()
        {

            int originalWidth = HiZBufferTex[0].GetSurfaceLevel(0).Description.Width;
            int originalHeight = HiZBufferTex[0].GetSurfaceLevel(0).Description.Height;

            //Transformed vertices don't need vertex shader execution.
            CustomVertex.TransformedTextured[] MipMapQuadVertices = new CustomVertex.TransformedTextured[4];

            d3dDevice.SetRenderState(RenderStates.ZEnable, false);
            d3dDevice.SetRenderState(RenderStates.ZBufferWriteEnable, false);

            d3dDevice.BeginScene();

            OcclusionEffect.Technique = "HiZBufferDownSampling";

            //Set the vertex format for the quad.
            d3dDevice.VertexFormat = CustomVertex.TransformedTextured.Format;


            for (int i = 1; i < mipLevels; i++)
            {

                //Get the Hierarchical zBuffer surface.
                //If it is even set 0 in the tex array otherwise if it is odd use the 1 in the array.
                Surface pHiZBufferSurface = HiZBufferTex[i % 2].GetSurfaceLevel(i);

                //Set the render target.
                d3dDevice.SetRenderTarget(0, pHiZBufferSurface);

                //d3dDevice.Clear(ClearFlags.Target | ClearFlags.ZBuffer, Color.Black, 1.0f, 0);

                //Viewport viewport = new Microsoft.DirectX.Direct3D.Viewport();
                //viewport.Width = pHiZBufferSurface.Description.Width;
                //viewport.Height = pHiZBufferSurface.Description.Height;
                //viewport.MaxZ = 1.0f;
                //viewport.MinZ = 0.0f;
                //viewport.X = 0;
                //viewport.Y = 0;
                //d3dDevice.Viewport = viewport;

                
                //Send the PS the previous size and mip level values.
                Vector4 LastMipInfo;
                LastMipInfo.X = originalWidth >> (i - 1); //The previous mipmap width.
                LastMipInfo.Y = originalHeight >> (i - 1);
                LastMipInfo.Z = i - 1; // previous mip level.
                LastMipInfo.W = 0;

                if (LastMipInfo.X == 0) LastMipInfo.X = 1;
                if (LastMipInfo.Y == 0) LastMipInfo.Y = 1;

                                 
                //Set the texture of the previous mip level.
                OcclusionEffect.SetValue("LastMipInfo", LastMipInfo);
                OcclusionEffect.SetValue("LastMip", HiZBufferTex[(i - 1) % 2]);

                //Update the mipmap vertices.
                UpdateMipMapVertices( ref MipMapQuadVertices, 0, 0, pHiZBufferSurface.Description.Width, pHiZBufferSurface.Description.Height);


                int numPasses = OcclusionEffect.Begin(0);

                for (int n = 0; n < numPasses; n++)
                {

                    OcclusionEffect.BeginPass(n);
                    d3dDevice.DrawUserPrimitives(PrimitiveType.TriangleFan, 2, MipMapQuadVertices);
                    OcclusionEffect.EndPass();
                }
                OcclusionEffect.End();


            }
            d3dDevice.EndScene();
        }

        private void DrawGeometryWithOcclusionEnabled()
        {


            d3dDevice.BeginScene();

            d3dDevice.SetRenderState(RenderStates.ZEnable, true);
            d3dDevice.SetRenderState(RenderStates.ZBufferWriteEnable, true);

            //Set screen as render target.
            d3dDevice.SetRenderTarget(0, pOldRT);
            d3dDevice.VertexFormat = oldVertexFormat;

            d3dDevice.Clear(ClearFlags.Target | ClearFlags.ZBuffer, Color.Black, 1.0f, 0);


            //TODO: See if this is needed anymore.
            d3dDevice.SetTexture(0, OccludeeDataTextureAABB);
            d3dDevice.SetTexture(1, OccludeeDataTextureDepth);
            d3dDevice.SetTexture(2, HiZBufferTex[0]);
            d3dDevice.SetTexture(3, OcclusionResultTex);


            DrawTeapots(true, "RenderWithOcclusionEnabled");


            d3dDevice.EndScene();
        }

        private void PerformOcclussionCulling()
        {

            d3dDevice.BeginScene();


            //Save the previous vertex format for later use.
            oldVertexFormat = d3dDevice.VertexFormat;


            //Set the vertex format for the quad.
            d3dDevice.VertexFormat = CustomVertex.TransformedTextured.Format;

            //TODO: See if this is needed anymore.
            d3dDevice.SetTexture(0, OccludeeDataTextureAABB);
            d3dDevice.SetTexture(1, OccludeeDataTextureDepth);
            d3dDevice.SetTexture(2, HiZBufferTex[0]);
            d3dDevice.SetTexture(3, OcclusionResultTex);

            d3dDevice.SetRenderTarget(0, OcclusionResultSurface);

            d3dDevice.SetRenderState(RenderStates.ZEnable, false);
            d3dDevice.SetRenderState(RenderStates.ZBufferWriteEnable, false);

            //Clear the result surface with 0 values, which mean they are "visible".
            d3dDevice.Clear(ClearFlags.Target, Color.FromArgb(0, 0, 0, 0), 1, 0);


            Matrix matWorldViewProj = d3dDevice.Transform.World * d3dDevice.Transform.View * d3dDevice.Transform.Projection;
            Matrix matWorldView = d3dDevice.Transform.World * d3dDevice.Transform.View;

            OcclusionEffect.SetValue("matWorldViewProj", matWorldViewProj);
            OcclusionEffect.SetValue("matWorldView", matWorldView);

            OcclusionEffect.SetValue("OccludeeDataTextureAABB", OccludeeDataTextureAABB);
            OcclusionEffect.SetValue("OccludeeDataTextureDepth", OccludeeDataTextureDepth);
            OcclusionEffect.SetValue("HiZBufferTex", HiZBufferTex[0]);
            OcclusionEffect.SetValue("maxOccludees", 100);

            OcclusionEffect.SetValue("HiZBufferWidth", (float)(HiZBufferTex[0].GetLevelDescription(0).Width));
            OcclusionEffect.SetValue("HiZBufferHeight", (float)(HiZBufferTex[0].GetLevelDescription(0).Height));

            OcclusionEffect.SetValue("maxMipLevels", HiZBufferTex[0].LevelCount); //Send number of mipmaps.


            //Set even and odd hierarchical z buffer textures.
            OcclusionEffect.SetValue("HiZBufferEvenTex", HiZBufferTex[0]);
            OcclusionEffect.SetValue("HiZBufferOddTex", HiZBufferTex[1]);

            if( OcclusionWithPyramid )
                OcclusionEffect.Technique = "OcclusionTestPyramid";
            else
                OcclusionEffect.Technique = "OcclusionTestSimple";

            int numPasses = OcclusionEffect.Begin(0);

            for (int n = 0; n < numPasses; n++)
            {

                OcclusionEffect.BeginPass(n);

                //Draw the quad making the pixel shaders inside of it execute.
                d3dDevice.DrawUserPrimitives(PrimitiveType.TriangleFan, 2, ScreenQuadVertices);

                OcclusionEffect.EndPass();
            }
            OcclusionEffect.End();

            d3dDevice.EndScene();

        }

        //Vertices for a screen aligned quad
        private void QuadVertexDeclaration()
        {

            ScreenQuadVertices = new CustomVertex.TransformedTextured[4];

            ScreenQuadVertices[0].Position = new Vector4(0f, 0f, 0f, 1f);
            ScreenQuadVertices[0].Rhw = 1.0f;
            ScreenQuadVertices[0].Tu = 0.0f;
            ScreenQuadVertices[0].Tv = 0.0f;

            ScreenQuadVertices[1].Position = new Vector4(TextureSize, 0f, 0f, 1f);
            ScreenQuadVertices[1].Rhw = 1.0f;
            ScreenQuadVertices[1].Tu = 1.0f;
            ScreenQuadVertices[1].Tv = 0.0f;


            ScreenQuadVertices[2].Position = new Vector4(TextureSize, TextureSize, 0f, 1f);
            ScreenQuadVertices[2].Rhw = 1.0f;
            ScreenQuadVertices[2].Tu = 1.0f;
            ScreenQuadVertices[2].Tv = 1.0f;

            ScreenQuadVertices[3].Position = new Vector4(0f, TextureSize, 0f, 1f);
            ScreenQuadVertices[3].Rhw = 1.0f;
            ScreenQuadVertices[3].Tu = 0.0f;
            ScreenQuadVertices[3].Tv = 1.0f;


        }

        private void DrawTeapots(bool withShader, string technique)
        {
            int index = 0;

            int rowSize = 100;
            for (int i = 0; i < rowSize; i++)
            {
                //for (int j = 0; j < 10; j++)
                {
                    d3dDevice.Transform.World = Matrix.Translation(i * 4, 0, 0);

                    if (withShader)
                    {
                        Matrix matWorldViewProj = d3dDevice.Transform.World * d3dDevice.Transform.View * d3dDevice.Transform.Projection;
                        Matrix matWorldView = d3dDevice.Transform.World * d3dDevice.Transform.View;

                        OcclusionEffect.SetValue("matWorldViewProj", matWorldViewProj);
                        OcclusionEffect.SetValue("matWorldView", matWorldView);

                        OcclusionEffect.SetValue("ocludeeIndexInTexture", index);
                        OcclusionEffect.SetValue("OcclusionResult", OcclusionResultTex);

                        OcclusionEffect.Technique = technique;
                        int numPasses = OcclusionEffect.Begin(0);

                        for (int n = 0; n < numPasses; n++)
                        {

                            OcclusionEffect.BeginPass(n);
                            teapot.DrawSubset(0);
                            OcclusionEffect.EndPass();
                        }
                        OcclusionEffect.End();

                        index++;
                    }
                    else
                    {
                        teapot.DrawSubset(0);
                    }
                }


            }

            d3dDevice.Transform.World = Matrix.Identity;
        }


        private void createOccludees()
        {

            //Get a texture size based on the max number of occludees.
            int textureSize = (int)Math.Sqrt(MAX_OCCLUDEES);

            float[] occludeeAABBdata = new float[MAX_OCCLUDEES * 4];

            float[] occludeeDepthData = new float[MAX_OCCLUDEES];


            float tempoccludeeSize = 4;

            //Populate Occludees AABB with random position and sizes.
            for (int i = 0; i < MAX_OCCLUDEES * 4; i += 4)
            {
                //x1, y1, x2, y2
                //TODO: Mati, mete codigo aca.
                occludeeAABBdata[i] = GuiController.Instance.D3dDevice.Viewport.Width / 2 - tempoccludeeSize/2; //r
                occludeeAABBdata[i + 1] = GuiController.Instance.D3dDevice.Viewport.Height / 2 - tempoccludeeSize / 2; //g
                occludeeAABBdata[i + 2] = GuiController.Instance.D3dDevice.Viewport.Width / 2 + tempoccludeeSize / 2; //b
                occludeeAABBdata[i + 3] = GuiController.Instance.D3dDevice.Viewport.Height / 2 + tempoccludeeSize / 2; //a

            }

            //Populate Occludees depth with random depth.
            //Here 0 means far and 1 is closest to the near plane.
            for (int i = 0; i < MAX_OCCLUDEES; i++)
            {
                //occludeeDepthData[i] = (float)rnd.NextDouble();
                //TODO: Mati, mete codigo aca.
                occludeeDepthData[i] = (float) i / 1000;
            }

            //TODO: Ver que hace esto por atras, a ver s se puede poner Usage.WriteOnly.

            //Stores the AABB in the texure as float32 x1,y1, x2, y2 
            OccludeeDataTextureAABB = new Texture(d3dDevice, textureSize, textureSize, 0, Usage.None, Format.A32B32G32R32F, Pool.Managed);
            GraphicsStream stream = OccludeeDataTextureAABB.LockRectangle(0, LockFlags.None);
            stream.Write(occludeeAABBdata);
            OccludeeDataTextureAABB.UnlockRectangle(0);


            //Stores the occludee depth as int8.
            OccludeeDataTextureDepth = new Texture(d3dDevice, textureSize, textureSize, 0, Usage.None, Format.R32F, Pool.Managed);

            stream = OccludeeDataTextureDepth.LockRectangle(0, LockFlags.None);
            stream.Write(occludeeDepthData);
            OccludeeDataTextureDepth.UnlockRectangle(0);


            QuadVertexDeclaration();

        }



        public override void close()
        {

        }

    }
}
