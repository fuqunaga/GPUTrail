﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace GpuTrailSystem
{
    /// <summary>
    /// Rendering GpuTrail
    /// 
    /// Processing flow:
    ///  GpuTrailAppendNode.UpdateInputBuffer() -(InputBuffer)-> GpuTrailAppendNode.AppendNode() -(TrailBuffer,NodeBuffer)-> 
    ///  [GpuTrailCulling -(TrailIndexBuffer)->] [GpuTrailRendering_CalcLod -(TrailIndexBufers)->] 
    ///  GpuTrailRendering_Lod.UpdateVertexBuffer()/UpdateArgsBuffer() -(VertexBuffer, ArgsBuffer) -> GpuTrailRendering_Lod.OnRenderObject()
    /// </summary>
    [RequireComponent(typeof(IGpuTrailAppendNode))]
    public class GpuTrailRenderer : MonoBehaviour
    {
        #region Type Define

        [System.Serializable]
        public class LodSetting
        {
            public float distance = 0f;
            public int lodNodeStep = 1; // Node steps to generate a vertex.　1:all nodes, 2:1/2 nodes, 3:1/3 nodes...
            public Material material;
        }

        #endregion


        public ComputeShader calcLodCS;
        public ComputeShader cullingCS;
        public ComputeShader updateVertexCS;

        public Material defaultMaterial;
        public float startWidth = 0.1f;
        public float endWidth = 0.1f;

        protected IGpuTrailAppendNode gpuTrailAppendNode;

        // Culling/CalcLod function can be customized.
        public Func<Camera, GpuTrail, float, GraphicsBuffer> calcTrailIndexBufferCulling;
        public Func<IEnumerable<float>, Camera, GpuTrail, IReadOnlyList<GraphicsBuffer>> calcTrailIndexBufferCalcLod;

        protected GpuTrailRenderer_Culling defaultCulling;
        protected GpuTrailRenderer_CalcLod defaultCalcLod;
        
        [SerializeField]
        protected List<LodSetting> lodSettings = new List<LodSetting>();
        protected List<GpuTrailRenderer_Lod> lodList = new List<GpuTrailRenderer_Lod>();

        protected Camera currentCameraOnRendering;


        [Header("Debug")]
        public bool cullingEnable = true;
        public bool updateVertexEnable = true;
        public bool renderingEnable = true;

        protected GpuTrail gpuTrail => gpuTrailAppendNode.GpuTrail;
        protected virtual Camera TargetCamera => Camera.main;


        #region Unity

        protected virtual void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        protected virtual void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        }

        protected virtual void Start()
        {
            if (gpuTrailAppendNode == null)
            {
                gpuTrailAppendNode = GetComponent<IGpuTrailAppendNode>();
            }

            if (!lodSettings.Any()) lodSettings.Add(new LodSetting());
        }


        protected virtual void LateUpdate()
        {
            if (lodSettings.Count != lodList.Count) ResetLodList();

            
            // AppendNode
            gpuTrailAppendNode.AppendNode();


            // Culling
            GraphicsBuffer trailIndexBufferCulling = null;
            if (cullingEnable)
            {
                if (calcTrailIndexBufferCulling == null)
                {
                    defaultCulling = new GpuTrailRenderer_Culling(cullingCS);
                    calcTrailIndexBufferCulling = defaultCulling.CalcTrailIndexBuffer;
                }

                float width = Mathf.Max(startWidth, endWidth);
                trailIndexBufferCulling = calcTrailIndexBufferCulling(TargetCamera, gpuTrail, width);
            }


            // CalcLod
            IReadOnlyList<GraphicsBuffer> trailIndexBuffersLod = null;
            bool needCalcLod = lodSettings.Count > 1;
            if (needCalcLod)
            {
                if (calcTrailIndexBufferCalcLod == null)
                {
                    defaultCalcLod = new GpuTrailRenderer_CalcLod(calcLodCS);
                    calcTrailIndexBufferCalcLod = defaultCalcLod.CalcTrailIndexBuffers;
                }

                trailIndexBuffersLod = calcTrailIndexBufferCalcLod(lodSettings.Select(setting => setting.distance), TargetCamera, gpuTrail);
            }


            // UpdateVertex
            if (updateVertexEnable)
            {
                for (var i = 0; i < lodList.Count; ++i)
                {
                    var lod = lodList[i];
                    var trailIndexBuffer = trailIndexBuffersLod?[i] ?? trailIndexBufferCulling;
                    lod.UpdateVertexBuffer(TargetCamera, startWidth, endWidth, trailIndexBuffer);
                }
            }

            // UpdateArgsBuffer
            for (var i = 0; i < lodList.Count; ++i)
            {
                var lod = lodList[i];
                var trailIndexBuffer = trailIndexBuffersLod?[i] ?? trailIndexBufferCulling;
                if (trailIndexBuffer != null)
                {
                    lod.UpdateArgsBuffer(trailIndexBuffer);
                }
                else
                {
                    lod.ResetArgsBuffer();
                }
            }
        }

        protected virtual void OnRenderObject()
        {
            if (Camera.current != null)
            {
                currentCameraOnRendering = Camera.current;
            }

            if ((currentCameraOnRendering == null) || (currentCameraOnRendering.cullingMask & (1 << gameObject.layer)) == 0)
            {
                return;
            }

            if (renderingEnable)
            {
                for(var i=0; i<lodList.Count; ++i)
                {
                    var lod = lodList[i];
                    var settings = lodSettings[i];

                    var material = settings.material;
                    if (material == null) material = defaultMaterial;

                    lod.OnRenderObject(material);
                }
            }
        }


        public virtual void OnDestroy()
        {
            DisposeLodList();
            defaultCulling?.Dispose();
            defaultCalcLod?.Dispose();
        }

        #endregion


        protected void ResetLodList()
        {
            DisposeLodList();

            lodList = lodSettings.Select(settings => new GpuTrailRenderer_Lod(gpuTrail, updateVertexCS, settings)).ToList();
        }

        void DisposeLodList()
        {
            lodList.ForEach(lod => lod.Dispose());
            lodList.Clear();
        }


        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            currentCameraOnRendering = camera;
        }



        #region Debug

        public void OnDrawGizmosSelected()
        {
            lodList.ForEach(lod => lod.OnDrawGizmosSelected());
        }

        #endregion
    }
}