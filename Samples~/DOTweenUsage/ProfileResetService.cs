namespace Ecanakli.Janitor.Samples.DOTweenUsage
{
    /// <summary>Resets the profile; it cancels a running apply through an intent method, not through the lifetime.</summary>
    public sealed class ProfileResetService
    {
        private readonly CustomizationService _customization;

        public ProfileResetService(CustomizationService customization) => _customization = customization;

        public void ResetProfile()
        {
            _customization.CancelApply();
            // reset profile data here
        }
    }
}
