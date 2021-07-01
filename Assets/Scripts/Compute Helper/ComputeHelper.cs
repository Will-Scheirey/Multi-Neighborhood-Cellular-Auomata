namespace ComputeShaderUtility
{
	using System;
	using System.Collections;
	using System.Collections.Generic;
	using System.Reflection;
	using UnityEditor;
	using UnityEngine;
	using UnityEngine.Experimental.Rendering;

	public static class ComputeHelper
	{
		public const FilterMode defaultFilterMode = FilterMode.Bilinear;
		public const GraphicsFormat defaultGraphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;

		static ComputeShader normalizeTextureCompute;
		static ComputeShader clearTextureCompute;
		static ComputeShader swizzleTextureCompute;

		public static event System.Action shouldReleaseEditModeBuffers;

		public static void Dispatch(ComputeShader cs, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1, int kernelIndex = 0)
		{
			Vector3Int threadgroupSizes = GetThreadGroupSizes(cs, kernelIndex);
			int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)threadgroupSizes.x);
			int numGroupsY = Mathf.CeilToInt(numIterationsY / (float)threadgroupSizes.y);
			int numGroupsZ = Mathf.CeilToInt(numIterationsZ / (float)threadgroupSizes.z);

			cs.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, int count)
		{
			int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
			bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;

			if (createNewBuffer)
			{
				Release(buffer);
				buffer = new ComputeBuffer(count, stride);
			}
		}

		public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, T[] data)
		{
			CreateStructuredBuffer<T>(ref buffer, data.Length);
			buffer.SetData(data);
		}

		public static ComputeBuffer CreateAndSetBuffer<T>(T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			ComputeBuffer buffer = null;
			CreateAndSetBuffer<T>(ref buffer, data, cs, nameID, kernelIndex);
			return buffer;
		}

		public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, T[] data, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(T));
			CreateStructuredBuffer<T>(ref buffer, data.Length);
			buffer.SetData(data);
			cs.SetBuffer(kernelIndex, nameID, buffer);
		}

		public static ComputeBuffer CreateAndSetBuffer<T>(int length, ComputeShader cs, string nameID, int kernelIndex = 0)
		{
			ComputeBuffer buffer = null;
			CreateAndSetBuffer<T>(ref buffer, length, cs, nameID, kernelIndex);
			return buffer;
		}

		public static void CreateAndSetBuffer<T>(ref ComputeBuffer buffer, int length, ComputeShader cs, String nameID, int kernelIndex = 0)
		{
			CreateStructuredBuffer<T>(ref buffer, length);
			cs.SetBuffer(kernelIndex, nameID, buffer);
		}

		//Release Buffers
		public static void Release(params ComputeBuffer[] buffers)
		{
			for (int i = 0; i < buffers.Length; i++)
			{
				if (buffers[i] != null)
					buffers[i].Release();
			}
		}


		public static Vector3Int GetThreadGroupSizes(ComputeShader compute, int kernelIndex = 0)
		{
			uint x, y, z;
			compute.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
			return new Vector3Int((int)x, (int)y, (int)z);
		}

		// Texture Helpers

		public static void CreateRenderTexture(ref RenderTexture texture, int width, int height)
		{
			CreateRenderTexture(ref texture, width, height, defaultFilterMode, defaultGraphicsFormat);
		}

		public static void CreateRenderTexture(ref RenderTexture texture, int width, int height, FilterMode filterMode, GraphicsFormat format)
		{
			if (texture == null || !texture.IsCreated() || texture.width != width || texture.height != height || texture.graphicsFormat != format)
			{
				if (texture != null)
					texture.Release();
				texture = new RenderTexture(width, height, 0);
				texture.graphicsFormat = format;
				texture.enableRandomWrite = true;

				texture.autoGenerateMips = false;
				texture.Create();
			}
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.filterMode = filterMode;
		}

		public static void CopyRenderTexture(Texture source, RenderTexture target)
		{
			Graphics.Blit(source, target);
		}

		public static void SwizzleTexture(Texture texture, Channel x, Channel y, Channel z, Channel w)
		{
			if (swizzleTextureCompute == null)
				swizzleTextureCompute = (ComputeShader)Resources.Load("Swizzle");
			swizzleTextureCompute.SetInt("width", texture.width);
			swizzleTextureCompute.SetInt("height", texture.height);
			swizzleTextureCompute.SetTexture(0, "Source", texture);
			swizzleTextureCompute.SetVector("x", ChannelToMask(x));
			swizzleTextureCompute.SetVector("y", ChannelToMask(y));
			swizzleTextureCompute.SetVector("z", ChannelToMask(z));
			swizzleTextureCompute.SetVector("w", ChannelToMask(w));
			Dispatch(swizzleTextureCompute, texture.width, texture.height, 1, 0);
		}

		public static void ClearRenderTexture(RenderTexture source, Vector4 clearColor)
		{
			if (clearTextureCompute == null)
				clearTextureCompute = (ComputeShader)Resources.Load("ClearTexture");
			clearTextureCompute.SetInt("width", source.width);
			clearTextureCompute.SetInt("height", source.height);
			clearTextureCompute.SetTexture(0, "Source", source);
			clearTextureCompute.SetVector("clearColor", clearColor);
			Dispatch(clearTextureCompute, source.width, source.height, 1, 0);
		}

		public static void NormalizeRenderTexture(RenderTexture source)
		{
			if (normalizeTextureCompute == null)
				normalizeTextureCompute = (ComputeShader)Resources.Load("NormalizeTexture");

			normalizeTextureCompute.SetInt("width", source.width);
			normalizeTextureCompute.SetInt("height", source.height);
			normalizeTextureCompute.SetTexture(0, "Source", source);
			normalizeTextureCompute.SetTexture(1, "Source", source);

			ComputeBuffer minMaxBuffer = CreateAndSetBuffer<int>(new int[] { int.MaxValue, 0 }, normalizeTextureCompute, "minMaxBuffer", 0);
			normalizeTextureCompute.SetBuffer(1, "minMaxBuffer", minMaxBuffer);

			Dispatch(normalizeTextureCompute, source.width, source.height, 1, 0);
			Dispatch(normalizeTextureCompute, source.width, source.height, 1, 1);

			Release(minMaxBuffer);
		}

		public static float[] PackFloats(params float[] values)
		{
			float[] packed = new float[values.Length * 4];
			for (int i = 0; i < values.Length; i++)
				packed[i * 4] = values[i];
			return values;
		}

		public static bool CanRunEditModeCompute
		{
			get
			{
				return CheckIfCanRunInEditMode();
			}
		}

		public static void SetParams(System.Object settings, ComputeShader shader, string variableNamePrefix = "", string variableNameSuffix = "")
		{
			var fields = settings.GetType().GetFields();
			foreach(var field in fields)
			{
				var fieldType = field.FieldType;
				string shaderVariableName = variableNamePrefix + field.Name + variableNameSuffix;

				if (fieldType == typeof(UnityEngine.Vector4) || fieldType == typeof(Vector3) || fieldType == typeof(Vector2))
				{
					shader.SetVector(shaderVariableName, (Vector4)field.GetValue(settings));
				}
				else if (fieldType == typeof(int))
				{
					shader.SetInt(shaderVariableName, (int)field.GetValue(settings));
				}
				else if (fieldType == typeof(float))
				{
					shader.SetFloat(shaderVariableName, (float)field.GetValue(settings));
				}
				else if (fieldType == typeof(bool))
				{
					shader.SetBool(shaderVariableName, (bool)field.GetValue(settings));
				}
				else
				{
					Debug.Log($"Type {fieldType} not implemented");
				}
			}
		}

		static Vector4 ChannelToMask(Channel channel)
		{
			switch(channel)
			{
				case Channel.Red:
					return new Vector4(1, 0, 0, 0);
				case Channel.Green:
					return new Vector4(0, 1, 0, 0);
				case Channel.Blue:
					return new Vector4(0, 0, 1, 0);
				case Channel.Alpha:
					return new Vector4(0, 0, 0, 1);
				case Channel.Zero:
					return new Vector4(0, 0, 0, 0);
				default:
					return Vector4.zero;
			}
		}

#if UNITY_EDITOR
		static UnityEditor.PlayModeStateChange playModeState;

		static ComputeHelper()
		{
			UnityEditor.EditorApplication.playModeStateChanged -= MonitorPlayModeState;
			UnityEditor.EditorApplication.playModeStateChanged += MonitorPlayModeState;

			UnityEditor.Compilation.CompilationPipeline.compilationStarted -= OnCompilationStarted;
			UnityEditor.Compilation.CompilationPipeline.compilationStarted += OnCompilationStarted;
		}

		static void MonitorPlayModeState(UnityEditor.PlayModeStateChange state)
		{
			playModeState = state;
			if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
			{
				if (shouldReleaseEditModeBuffers != null)
					shouldReleaseEditModeBuffers();
			}
		}
		
		static void OnCompilationStarted(System.Object obj)
		{
			if (shouldReleaseEditModeBuffers != null)
				shouldReleaseEditModeBuffers();
		}
#endif

		static bool CheckIfCanRunInEditMode()
		{
			bool isCompilingOrExitingEditMode = false;
#if UNITY_EDITOR
			isCompilingOrExitingEditMode |= UnityEditor.EditorApplication.isCompiling;
			isCompilingOrExitingEditMode |= playModeState == UnityEditor.PlayModeStateChange.ExitingEditMode;
#endif
			bool canRun = !isCompilingOrExitingEditMode;
			return canRun;
		}
	}
}