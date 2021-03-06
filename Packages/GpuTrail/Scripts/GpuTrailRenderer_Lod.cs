using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.XR;

namespace GpuTrailSystem
{
    public class GpuTrailRenderer_Lod : IDisposable
    {
        #region Static

        public static class CSParam
        {
            public static readonly string Kernel_UpdateVertex = "UpdateVertex";

            public static readonly int Time = Shader.PropertyToID("_Time");
            public static readonly int ToCameraDir = Shader.PropertyToID("_ToCameraDir");
            public static readonly int CameraPos = Shader.PropertyToID("_CameraPos");
            public static readonly int StartWidth = Shader.PropertyToID("_StartWidth");
            public static readonly int EndWidth = Shader.PropertyToID("_EndWidth");
            public static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");
            public static readonly int LodNodeStep = Shader.PropertyToID("_LodNodeStep");


            public static readonly string Kernel_ArgsBufferMultiply = "ArgsBufferMultiply";
            public static readonly int ArgsBuffer = Shader.PropertyToID("_ArgsBuffer");
        }

        public static class ShaderParam
        {
            public static readonly int StartWidth = Shader.PropertyToID("_StartWidth");
            public static readonly int EndWidth = Shader.PropertyToID("_EndWidth");
            public static readonly int VertexNumPerTrail = Shader.PropertyToID("_VertexNumPerTrail");
            public static readonly int VertexBuffer = Shader.PropertyToID("_VertexBuffer");

            public static readonly int TrailIndexBuffer = Shader.PropertyToID("_TrailIndexBuffer");
        }

        #endregion


        protected readonly GpuTrail gpuTrail;
        protected readonly ComputeShader computeShader;
        protected readonly GpuTrailRenderer.LodSetting lodSetting;
        protected readonly GpuTrailIndexDispatcher gpuTrailIndexDispatcher = new GpuTrailIndexDispatcher();

        protected GraphicsBuffer vertexBuffer;
        protected GraphicsBuffer indexBuffer;
        protected GraphicsBuffer argsBuffer;


        int lodNodeStep => lodSetting.lodNodeStep;

        public int nodeNumPerTrailWithLod => gpuTrail.nodeNumPerTrail / lodNodeStep;
        public int vertexNumPerTrail => nodeNumPerTrailWithLod * 2;
        public int vertexBufferSize => gpuTrail.trailNum * vertexNumPerTrail;
        public int indexNumPerTrail => (nodeNumPerTrailWithLod - 1) * 6;

        public GpuTrailRenderer_Lod(GpuTrail gpuTrail, ComputeShader computeShader, GpuTrailRenderer.LodSetting lodSetting)
        {
            this.gpuTrail = gpuTrail;
            this.computeShader = computeShader;
            this.lodSetting = lodSetting;
        }


        public void Dispose()
        {
            ReleaseBuffers();
            gpuTrailIndexDispatcher.Dispose();
        }


        protected void InitBufferIfNeed()
        {
            if ((vertexBuffer != null) && (vertexBuffer.count == vertexBufferSize))
            {
                return;
            }

            Assert.IsTrue(0 < lodNodeStep && lodNodeStep < gpuTrail.nodeNumPerTrail, $"Invalid lodNodeStep[{lodNodeStep}]");


            ReleaseBuffers();

            vertexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertexBufferSize, Marshal.SizeOf<Vertex>()); // 1 node to 2 vtx(left,right)
            vertexBuffer.SetData(Enumerable.Repeat(default(Vertex), vertexBuffer.count).ToArray());

            // 各Nodeの最後と次のNodeの最初はポリゴンを繋がないので-1
            var indexData = new int[indexNumPerTrail];
            var iidx = 0;
            for (var iNode = 0; iNode < nodeNumPerTrailWithLod - 1; ++iNode)
            {
                var offset = iNode * 2;
                indexData[iidx++] = 0 + offset;
                indexData[iidx++] = 1 + offset;
                indexData[iidx++] = 2 + offset;
                indexData[iidx++] = 2 + offset;
                indexData[iidx++] = 1 + offset;
                indexData[iidx++] = 3 + offset;
            }

            indexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Index | GraphicsBuffer.Target.Structured, indexData.Length, Marshal.SizeOf<uint>()); // 1 node to 2 triangles(6vertexs)
            indexBuffer.SetData(indexData);

            argsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(uint));
            ResetArgsBuffer();
        }

        protected void ReleaseBuffers()
        {
            vertexBuffer?.Release();
            indexBuffer?.Release();
            argsBuffer?.Release();

            vertexBuffer = null;
            indexBuffer = null;
            argsBuffer = null;
        }

        public void UpdateVertexBuffer(Camera camera, float startWidth, float endWidth, GraphicsBuffer trailIndexBuffer)
        {
            InitBufferIfNeed();

            var toCameraDir = default(Vector3);
            if (camera.orthographic)
            {
                toCameraDir = -camera.transform.forward;
            }

            computeShader.SetFloat(CSParam.Time, Time.time);

            computeShader.SetVector(CSParam.ToCameraDir, toCameraDir);
            computeShader.SetVector(CSParam.CameraPos, camera.transform.position);

            computeShader.SetFloat(CSParam.StartWidth, startWidth);
            computeShader.SetFloat(CSParam.EndWidth, endWidth);
            computeShader.SetInt(CSParam.LodNodeStep, lodNodeStep);

            var kernel = computeShader.FindKernel(CSParam.Kernel_UpdateVertex);
            gpuTrail.SetCSParams(computeShader, kernel);
            computeShader.SetBuffer(kernel, CSParam.VertexBuffer, vertexBuffer);

            if (trailIndexBuffer != null)
            {
                gpuTrailIndexDispatcher.Dispatch(computeShader, kernel, trailIndexBuffer);
            }
            else
            {
                gpuTrailIndexDispatcher.Dispatch(computeShader, kernel, gpuTrail.trailNum);
            }



#if false
            var trails = new Trail[gpuTrail.trailBuffer.count];
            gpuTrail.trailBuffer.GetData(trails);
            var lastNodeIdx = trails[0].totalInputNum % gpuTrail.nodeNumPerTrail;

            var nodes = new Node[gpuTrail.nodeBuffer.count];
            gpuTrail.nodeBuffer.GetData(nodes);
            //nodes = nodes.Take(100).ToArray();
            var idxAndNodes = Enumerable.Range(0, nodes.Length)
                .Zip(nodes, (i, node) => new { i, node })
                .OrderByDescending(iNode => iNode.node.time)
                .ToList();
                
            var vtxs = new Vertex[vertexBuffer.count];
            vertexBuffer.GetData(vtxs);
            //vtxs = vtxs.Take(100).ToArray();
            vtxs = vtxs.ToArray();
            for (var i = 0; i < vtxs.Length; ++i)
            {
                if (vtxs[i].pos == Vector3.zero)
                {
                    Debug.Log(i);
                }
            }
#endif
        }


        // SinglePassInstanced requires you to manually double the number of instances
        // https://docs.unity3d.com/Manual/SinglePassInstancing.html
        protected bool IsSinglePassInstancedRendering => XRSettings.enabled && XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.SinglePassInstanced;

        public void UpdateArgsBuffer(GraphicsBuffer trailIndexBuffer)
        {
            InitBufferIfNeed();

            GraphicsBuffer.CopyCount(trailIndexBuffer, argsBuffer, 4);

            if (IsSinglePassInstancedRendering)
            {
                var kernel_argsBufferMultiply = computeShader.FindKernel(CSParam.Kernel_ArgsBufferMultiply);
                computeShader.SetBuffer(kernel_argsBufferMultiply, CSParam.ArgsBuffer, argsBuffer);

                computeShader.Dispatch(kernel_argsBufferMultiply, 1, 1, 1);
            }

            /*
            var data = new int[4];
            argsBuffer.GetData(data);
            Debug.Log($"{data[0]} {data[1]} {data[2]} {data[3]}");
            */
        }

        int[] tmpArgsData;

        public void ResetArgsBuffer()
        {
            InitBufferIfNeed();

            if (tmpArgsData == null) tmpArgsData = new int[5];

            tmpArgsData[0] = indexNumPerTrail;
            tmpArgsData[1] = gpuTrail.trailNum * (IsSinglePassInstancedRendering ? 2 : 1);
            tmpArgsData[2] = 0;
            tmpArgsData[3] = 0;
            tmpArgsData[4] = 0;

            argsBuffer.SetData(tmpArgsData); // int[4]{ indexNumPerTrail, trailNum, 0, 0}
        }


        public void OnRenderObject(Material material, float startWidth, float endWidth)
        {
            material.SetFloat(ShaderParam.StartWidth, startWidth);
            material.SetFloat(ShaderParam.EndWidth, endWidth);
            material.SetInt(ShaderParam.VertexNumPerTrail, vertexNumPerTrail);
            material.SetBuffer(ShaderParam.VertexBuffer, vertexBuffer);

            for (var i = 0; i < material.passCount; ++i)
            {
                material.SetPass(i);
                Graphics.DrawProceduralIndirectNow(MeshTopology.Triangles, indexBuffer, argsBuffer);
            }
        }


        #region Debug

        public bool debugDrawVertexBuf;

        public void OnDrawGizmosSelected()
        {
            if (debugDrawVertexBuf)
            {
                var defaultColor = Color.yellow;
                Gizmos.color = defaultColor;

                var data = new Vertex[vertexBuffer.count];
                vertexBuffer.GetData(data);

                var num = vertexBuffer.count / 2;
                for (var i = 0; i < num; ++i)
                {
                    Color? tmpColor = null;
                    if (i == 0) { tmpColor = Color.red; }
                    if (i == num - 1) { tmpColor = Color.green; }

                    if (tmpColor.HasValue)
                    {
                        Gizmos.color = tmpColor.Value;

                    }

                    var v0 = data[2 * i];
                    var v1 = data[2 * i + 1];

                    Gizmos.DrawLine(v0.pos, v1.pos);

                    if (tmpColor.HasValue)
                    {
                        Gizmos.color = defaultColor;
                    }
                }
            }
        }

        #endregion
    }
}