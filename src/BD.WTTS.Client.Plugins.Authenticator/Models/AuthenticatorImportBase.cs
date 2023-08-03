namespace BD.WTTS.Models;

public abstract partial class AuthenticatorImportBase : IAuthenticatorImport
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    public abstract ResIcon IconName { get; }

    public abstract ICommand AuthenticatorImportCommand { get; set; }

    string? _currentPassword { get; set; }

    public async Task<bool> VerifyMaxValue()
    {
        var auths = await AuthenticatorHelper.GetAllSourceAuthenticatorAsync();
        if (auths.Length < IAccountPlatformAuthenticatorRepository.MaxValue) return true;
        Toast.Show(ToastIcon.Info, Strings.Info_AuthMaximumQuantity);
        return false;
    }

    public async Task SaveAuthenticator(IAuthenticatorDTO authenticatorDto)
    {
        var sourceList = await AuthenticatorHelper.GetAllSourceAuthenticatorAsync();

        var (hasLocalPcEncrypt, hasPasswordEncrypt) = AuthenticatorHelper.HasEncrypt(sourceList);

        if (hasPasswordEncrypt && string.IsNullOrEmpty(_currentPassword))
        {
            var textViewmodel = new TextBoxWindowViewModel()
            {
                InputType = TextBoxWindowViewModel.TextBoxInputType.Password,
            };
            if (await IWindowManager.Instance.ShowTaskDialogAsync(textViewmodel, Strings.Title_InputAuthPassword, isDialog: false,
                    isCancelButton: true) &&
                textViewmodel.Value != null)
            {
                if (await AuthenticatorHelper.ValidatePassword(sourceList[0], textViewmodel.Value))
                {
                    _currentPassword = textViewmodel.Value;
                }
                else
                {
                    Toast.Show(ToastIcon.Warning, Strings.Warning_PasswordError);
                    return;
                }
            }
            else return;
        }

        await AuthenticatorHelper.AddOrUpdateSaveAuthenticatorsAsync(authenticatorDto, _currentPassword,
            hasLocalPcEncrypt);
    }
}