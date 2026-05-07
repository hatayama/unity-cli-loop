namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test class for another assembly (safe API version)
    /// Used in DynamicAssemblyAdditionTests
    /// Unlike ForDynamicAssemblyTest, does not use dangerous APIs
    /// </summary>
    public class DynamicAssemblyTest
    {
        public string HelloWorld()
        {
            return "Hello from DynamicAssemblyTest";
        }
        
        public int Add(int a, int b)
        {
            return a + b;
        }
        
        public string GetAssemblyName()
        {
            return GetType().Assembly.GetName().Name;
        }

        public void ExecuteAnoterInstanceMethod()
        {
            ForDynamicAssemblyTest a = new();
            a.TestForbiddenOperationsInAnotherDLL();
        }
    }
}