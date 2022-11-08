using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Speckle.ConnectorUnity.Components;
using Speckle.Core.Api;
using Speckle.Core.Models;
using UnityEditor;
using UnityEngine;

#nullable enable
namespace Speckle.ConnectorUnity.Editor
{
    [CustomEditor(typeof(SpeckleReceiver))]
    [CanEditMultipleObjects]
    public class SpeckleReceiverEditor : UnityEditor.Editor
    {
        private CancellationTokenSource? tokenSource;
        public override async void OnInspectorGUI()
        {
            var speckleReceiver = (SpeckleReceiver) target;
            DrawDefaultInspector();
            
            bool receive = GUILayout.Button("Receive!");
                
            if (receive)
            {
                await Receive(speckleReceiver);
            }
        }

        public async Task<GameObject?> Receive(SpeckleReceiver speckleReceiver)
        {
            tokenSource?.Cancel();
            if (!speckleReceiver.GetSelection(out Client? client, out _, out Commit? commit, out string? error))
            {
                Debug.LogWarning($"Not ready to receive: {error}", speckleReceiver);
                return null;
            }

            tokenSource = new CancellationTokenSource();
            Base? commitObject = await ReceiveCommit(speckleReceiver, client.ServerUrl);

            if (commitObject == null) return null;
            
            var gameObject = Convert(speckleReceiver, commitObject, commit.id);
            Debug.Log($"Successfully received and converted {commit.id}", target);
            return gameObject;
        }

        private GameObject Convert(SpeckleReceiver receiver, Base commitObject, string name)
        {
            //Convert Speckle Objects
            int childrenConverted = 0;
            float totalChildren = commitObject.totalChildrenCount; 
            
            void BeforeConvertCallback(Base b)
            {
                //TODO: this is an incorrect way of measuring progress, as totalChildren != total convertable children
                float progress = childrenConverted++ / totalChildren;
                
                EditorUtility.DisplayProgressBar("Converting To Native...", 
                    $"{b.speckle_type} - {b.id}",
                    progress);
            }

            var go = receiver.ConvertToNativeWithCategories(commitObject,
                name, BeforeConvertCallback);
            go.transform.SetParent(receiver.transform);
            return go;
        }
        

        private async Task<Base?> ReceiveCommit(SpeckleReceiver speckleReceiver, string serverLogName)
        {
            string message = $"Receiving data from {serverLogName}...";
            EditorUtility.DisplayProgressBar(message, "", 0);

            var totalObjectCount = 1;
            void OnTotalChildrenKnown(int count)
            {
                totalObjectCount = count;
            };
            
            void OnProgress(ConcurrentDictionary<string, int> dict)
            {
                var currentProgress = dict.Values.Average();
                var progress = (float) currentProgress / totalObjectCount;
                EditorApplication.delayCall += () =>
                {
                    bool shouldCancel = EditorUtility.DisplayCancelableProgressBar(message, 
                        $"{currentProgress}/{totalObjectCount}",
                        progress);
                    
                    if (shouldCancel)
                    {
                        CancelReceive();
                    }
                };
            };
            
            void OnError(string message, Exception e)
            {
                if (e is not OperationCanceledException)
                {
                    Debug.LogError($"Receive failed: {message}\n{e}", speckleReceiver);
                }
                CancelReceive();
            };

            Base? commitObject = null;
            try
            {
                speckleReceiver.OnTotalChildrenCountKnown.AddListener(OnTotalChildrenKnown);
                speckleReceiver.OnReceiveProgressAction.AddListener(OnProgress);
                speckleReceiver.OnErrorAction.AddListener(OnError);
                commitObject = await speckleReceiver.ReceiveAsync(tokenSource?.Token ?? CancellationToken.None);
                if (commitObject == null)
                {
                    Debug.LogWarning($"Receive warning: Receive operation returned null", speckleReceiver);
                }
            }
            finally
            {
                speckleReceiver.OnTotalChildrenCountKnown.RemoveListener(OnTotalChildrenKnown);
                speckleReceiver.OnReceiveProgressAction.RemoveListener(OnProgress);
                speckleReceiver.OnErrorAction.RemoveListener(OnError);
                EditorApplication.delayCall += EditorUtility.ClearProgressBar;
            }

            return commitObject;
        }

        private void CancelReceive()
        {
            tokenSource?.Cancel();
            EditorApplication.delayCall += EditorUtility.ClearProgressBar;
        }

        private void OnDestroy()
        {
            tokenSource?.Cancel();
        }
    }
}
