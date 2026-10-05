using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using WallpaperScheduler.Application;
using WallpaperScheduler.Domain;

namespace WallpaperScheduler.App;

public partial class NetworkSettingsWindow : Window
{
    private readonly IConfigStore _configStore;
    private AppConfig? _config;

    public NetworkSettingsWindow(IConfigStore configStore)
    {
        _configStore = configStore;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _config = await _configStore.LoadAsync();
        var n = _config.Network;
        RoleBox.SelectedIndex = n.Role switch
        {
            NetworkNodeRole.Controller => 1,
            NetworkNodeRole.Agent => 2,
            _ => 0
        };
        NodeNameBox.Text = n.NodeName;
        GroupBox.Text = n.Group;
        PortBox.Text = n.ControllerPort.ToString();
        SecretBox.Text = n.SharedSecret;
        ControllerUrlBox.Text = n.ControllerUrl;
        AgentSecretBox.Text = n.SharedSecret;
        UpdatePanels();
    }

    private void RoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePanels();

    private void UpdatePanels()
    {
        if (ControllerPanel is null || AgentPanel is null) return;
        ControllerPanel.Visibility = RoleBox.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        AgentPanel.Visibility = RoleBox.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void GenerateSecret_Click(object sender, RoutedEventArgs e)
    {
        SecretBox.Text = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _config ??= await _configStore.LoadAsync();
        var network = _config.Network;

        network.Role = RoleBox.SelectedIndex switch
        {
            1 => NetworkNodeRole.Controller,
            2 => NetworkNodeRole.Agent,
            _ => NetworkNodeRole.Standalone
        };
        network.NodeName = string.IsNullOrWhiteSpace(NodeNameBox.Text) ? Environment.MachineName : NodeNameBox.Text.Trim();
        network.Group = string.IsNullOrWhiteSpace(GroupBox.Text) ? "default" : GroupBox.Text.Trim();

        if (network.Role == NetworkNodeRole.Controller)
        {
            if (!int.TryParse(PortBox.Text, out var port) || port is < 1024 or > 65535)
            {
                System.Windows.MessageBox.Show(this, "Informe uma porta entre 1024 e 65535.", "Rede", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            network.ControllerPort = port;
            network.SharedSecret = SecretBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(network.SharedSecret))
                network.SharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        }
        else if (network.Role == NetworkNodeRole.Agent)
        {
            network.ControllerUrl = ControllerUrlBox.Text.Trim().TrimEnd('/');
            network.SharedSecret = AgentSecretBox.Text.Trim();

            if (!Uri.TryCreate(network.ControllerUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                System.Windows.MessageBox.Show(this, "Informe o endereço do Controller, por exemplo http://192.168.1.10:48721.", "Rede", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(network.SharedSecret))
            {
                System.Windows.MessageBox.Show(this, "Copie para este Agent a chave de pareamento exibida no Controller.", "Rede", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        await _configStore.SaveAsync(_config);
        System.Windows.MessageBox.Show(this, "Configuração de rede salva. Reinicie o Wallpaper Scheduler para aplicar o papel desta máquina.", "Rede", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
