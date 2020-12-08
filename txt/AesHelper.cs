using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
//https://www.cnblogs.com/wolf-sun/p/3380453.html
public static class AesHelper
{
    #region 秘钥对

    private const string saltString = "Wolfy@home";
    private const string pWDString = "home@Wolfy";

    #endregion

    #region 加/解密算法

    /// <summary>
    /// 解密
    /// </summary>
    /// <param name="sSource">需要解密的内容</param>
    /// <returns></returns>
    public static string DecryptString(string strSource)
    {
        byte[] encryptBytes = Convert.FromBase64String(strSource);
        byte[] salt = Encoding.UTF8.GetBytes(saltString);
        //提供高级加密标准 (AES) 对称算法的托管实现。
        AesManaged aes = new AesManaged();
        //通过使用基于 System.Security.Cryptography.HMACSHA1 的伪随机数生成器，实现基于密码的密钥派生功能 (PBKDF2)。
        Rfc2898DeriveBytes rfc = new Rfc2898DeriveBytes(pWDString, salt);
        // 获取或设置加密操作的块大小（以位为单位）。
        aes.BlockSize = aes.LegalBlockSizes[0].MaxSize;
        //获取或设置用于对称算法的密钥大小（以位为单位）。
        aes.KeySize = aes.LegalKeySizes[0].MaxSize;
        //获取或设置用于对称算法的密钥。
        aes.Key = rfc.GetBytes(aes.KeySize / 8);
        //获取或设置用于对称算法的初始化向量 (IV)。
        aes.IV = rfc.GetBytes(aes.BlockSize / 8);

        // 用当前的 Key 属性和初始化向量 IV 创建对称解密器对象
        System.Security.Cryptography.ICryptoTransform decryptTransform = aes.CreateDecryptor();

        // 解密后的输出流
        MemoryStream decryptStream = new MemoryStream();

        // 将解密后的目标流（decryptStream）与解密转换（decryptTransform）相连接
        CryptoStream decryptor = new CryptoStream(
            decryptStream, decryptTransform, CryptoStreamMode.Write);

        // 将一个字节序列写入当前 CryptoStream （完成解密的过程）
        decryptor.Write(encryptBytes, 0, encryptBytes.Length);
        decryptor.Close();

        // 将解密后所得到的流转换为字符串
        return Convert.ToBase64String(decryptStream.ToArray());

    }

    /// <summary>
    /// 加密
    /// </summary>
    /// <param name="sSource">需要加密的内容</param>
    /// <returns></returns>
    public static string EncryptString(string strSource)
    {
        byte[] data = UTF8Encoding.UTF8.GetBytes(strSource);
        byte[] salt = UTF8Encoding.UTF8.GetBytes(saltString);

        // AesManaged - 高级加密标准(AES) 对称算法的管理类
        AesManaged aes = new AesManaged();

        // Rfc2898DeriveBytes - 通过使用基于 HMACSHA1 的伪随机数生成器，实现基于密码的密钥派生功能 (PBKDF2 - 一种基于密码的密钥派生函数)
        // 通过 密码 和 salt 派生密钥
        Rfc2898DeriveBytes rfc = new Rfc2898DeriveBytes(pWDString, salt);

        /*
        * AesManaged.BlockSize - 加密操作的块大小（单位：bit）
        * AesManaged.LegalBlockSizes - 对称算法支持的块大小（单位：bit）
        * AesManaged.KeySize - 对称算法的密钥大小（单位：bit）
        * AesManaged.LegalKeySizes - 对称算法支持的密钥大小（单位：bit）
        * AesManaged.Key - 对称算法的密钥
        * AesManaged.IV - 对称算法的密钥大小
        * Rfc2898DeriveBytes.GetBytes(int 需要生成的伪随机密钥字节数) - 生成密钥
        */

        aes.BlockSize = aes.LegalBlockSizes[0].MaxSize;
        aes.KeySize = aes.LegalKeySizes[0].MaxSize;
        aes.Key = rfc.GetBytes(aes.KeySize / 8);
        aes.IV = rfc.GetBytes(aes.BlockSize / 8);

        // 用当前的 Key 属性和初始化向量 IV 创建对称加密器对象
        ICryptoTransform encryptTransform = aes.CreateEncryptor();

        // 加密后的输出流
        MemoryStream encryptStream = new MemoryStream();

        // 将加密后的目标流（encryptStream）与加密转换（encryptTransform）相连接
        CryptoStream encryptor = new CryptoStream
            (encryptStream, encryptTransform, CryptoStreamMode.Write);

        // 将一个字节序列写入当前 CryptoStream （完成加密的过程）
        encryptor.Write(data, 0, data.Length);
        encryptor.Close();

        return Encoding.UTF8.GetString(encryptStream.ToArray());
    }

    #endregion
}