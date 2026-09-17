using Core.Entities;
using RuriLib.Models.Proxies;

namespace Core.Helpers;

// TODO: Refactor these methods, make it so that you can call Map<TInput,TOutput>
public static class Mapper
{
    /// <summary>
    /// Maps a <see cref="Proxy"/> to a <see cref="ProxyEntity"/>.
    /// </summary>
    public static ProxyEntity MapProxyToProxyEntity(Proxy proxy) => new()
    {
        Country = proxy.Country,
        Host = proxy.Host,
        Port = proxy.Port,
        Username = proxy.Username,
        Password = proxy.Password,
        Ping = proxy.Ping,
        Status = proxy.WorkingStatus,
        Type = proxy.Type
    };
}
