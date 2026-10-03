#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace AnimalCombat.ReplayViewer.Tests.EditMode
{
    [InitializeOnLoad]
    public static class ReplayViewerTestAutomation
    {
        const string ResultPath = "TestResults/PlayModeGameFirst.txt";

        static ReplayViewerTestAutomation()
        {
            TestRunnerApi.RegisterTestCallback(new ResultCallbacks(), -1000);
        }

        [MenuItem("Tools/Replay Viewer/Run PlayMode Acceptance")]
        public static void RunPlayModeAcceptance()
        {
            string absoluteResultPath = Path.GetFullPath(ResultPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteResultPath));
            if (File.Exists(absoluteResultPath))
                File.Delete(absoluteResultPath);

            TestRunnerApi api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.PlayMode,
                assemblyNames = new[] { "AnimalCombat.ReplayViewer.Tests.PlayMode" }
            }));
        }

        sealed class ResultCallbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                if (result.Test.TestMode != TestMode.PlayMode || !ContainsPlayModeAssembly(result))
                    return;

                string summary =
                    $"status={result.TestStatus}\n" +
                    $"pass={result.PassCount}\n" +
                    $"fail={result.FailCount}\n" +
                    $"skip={result.SkipCount}\n" +
                    $"inconclusive={result.InconclusiveCount}\n";
                string absoluteResultPath = Path.GetFullPath(ResultPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absoluteResultPath));
                File.WriteAllText(absoluteResultPath, summary);
                Debug.Log($"Replay Viewer PlayMode: {summary.Replace('\n', ' ')}");
            }

            static bool ContainsPlayModeAssembly(ITestResultAdaptor result)
            {
                if (result.Test.IsTestAssembly &&
                    result.Name.Contains("AnimalCombat.ReplayViewer.Tests.PlayMode"))
                    return true;

                foreach (ITestResultAdaptor child in result.Children)
                {
                    if (ContainsPlayModeAssembly(child))
                        return true;
                }

                return false;
            }
        }
    }
}
#endif
