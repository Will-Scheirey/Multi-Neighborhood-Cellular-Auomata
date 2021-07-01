using ComputeShaderUtility;
using System;
using System.Collections.Generic;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.PlayerLoop;
using UnityEngine.SocialPlatforms;
using UnityEngine.UI;
using UnityEngine.XR;

public class Main : MonoBehaviour
{
    public ComputeShader compute;

    [Header("Display Settings")]
    public int width = 1920;
    public int height = 1080;
    public FilterMode filterMode = FilterMode.Point;
    public GraphicsFormat graphicsFormat = ComputeHelper.defaultGraphicsFormat;

    [SerializeField, HideInInspector] protected RenderTexture displayTexture;
    private ComputeBuffer buffer;

    struct Node
    {
        public float lastState;
        public float currentState;
        public float nextState;
    }

    protected virtual void Start()
    {
        Init();
        transform.GetComponentInChildren<MeshRenderer>().material.mainTexture = displayTexture;
        //Application.targetFrameRate = 1;
    }

    private void OnDisable()
    {
        if (buffer != null)
            buffer.Release();
    }

    public float Random(float min, float max)
    {
        return UnityEngine.Random.Range(min, max);
    }

    private void SetUpScene()
    {
        Node[] nodes = new Node[width*height];
        for (int i = 0; i < width; i++)
        {
            for (int j = 0; j < height; j++)
            {
                float d = Mathf.Sqrt(Mathf.Pow(width / 2 - i, 2) + Mathf.Pow(height / 2 - j, 2));
                int state = 0;
                //if (d <= 100)
                    state = Random(0, 1) <= 0.5f ? 0 : 1;
                nodes[j + i * height] = new Node() { lastState = state, currentState = state};
            }
        }
        ComputeHelper.CreateAndSetBuffer(ref buffer, nodes, compute, "buffer");
    }

    private void Init()
    {
        SetUpScene();
        ComputeHelper.CreateRenderTexture(ref displayTexture, width, height, filterMode, graphicsFormat);

        compute.SetInt("width", width);
        compute.SetInt("height", height);

        compute.SetBuffer(0, "buffer", buffer);

        compute.SetTexture(0, "Source", displayTexture);
    }

    private void FixedUpdate()
    {
            RunSimulation();
    }

    private void RunSimulation()
    {
        ComputeHelper.Dispatch(compute, width, height, 1, 0);
    }

}
