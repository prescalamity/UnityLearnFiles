using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

class RSAUtil1 {

    /// <summary>
    /// 使用DES加密指定字符串
    /// </summary>
    /// <param name="encryptStr">待加密的字符串</param>
    /// <param name="key">密钥(最大长度8)</param>
    /// <param name="IV">初始化向量(最大长度8)</param>
    /// <returns>加密后的字符串</returns>
    public static string DESEncrypt(string encryptStr, string key="DAZZLE21", string IV= "DAZZLE@!")
    {
        //将key和IV处理成8个字符
        key += "12345678";
        IV += "12345678";
        key = key.Substring(0, 8);
        IV = IV.Substring(0, 8);
        SymmetricAlgorithm sa;
        ICryptoTransform ict;
        MemoryStream ms;
        CryptoStream cs;
        byte[] byt;
        sa = new DESCryptoServiceProvider();
        sa.Key = Encoding.UTF8.GetBytes(key);
        sa.IV = Encoding.UTF8.GetBytes(IV);
        ict = sa.CreateEncryptor();

        //byt = Encoding.UTF8.GetBytes(encryptStr);
        byt = new UnicodeEncoding().GetBytes(encryptStr);

        ms = new MemoryStream();
        cs = new CryptoStream(ms, ict, CryptoStreamMode.Write);
        cs.Write(byt, 0, byt.Length);
        cs.FlushFinalBlock();
        cs.Close();
        //加上一些干扰字符
        string retVal = Convert.ToBase64String(ms.ToArray());
        System.Random ra = new Random();
        for (int i = 0; i < 8; i++)
        {
            int radNum = ra.Next(36);
            char radChr = Convert.ToChar(radNum + 65);//生成一个随机字符
            retVal = retVal.Substring(0, 2 * i + 1) + radChr.ToString() + retVal.Substring(2 * i + 1);
        }
        return retVal;
    }

    /// <summary>
    /// 使用DES解密指定字符串
    /// </summary>
    /// <param name="encryptedValue">待解密的字符串</param>
    /// <param name="key">密钥(最大长度8)</param>
    /// <param name="IV">初始化向量(最大长度8)</param>
    /// <returns>解密后的字符串</returns>
    public static string DESDecrypt(string encryptedValue, string key = "DAZZLE21", string IV = "DAZZLE@!")
    {
        //去掉干扰字符
        string tmp = encryptedValue;
        if (tmp.Length < 16)
        {
            return "";
        }
        for (int i = 0; i < 8; i++)
        {
            tmp = tmp.Substring(0, i + 1) + tmp.Substring(i + 2);
        }
        encryptedValue = tmp;
        //将key和IV处理成8个字符
        key += "12345678";
        IV += "12345678";
        key = key.Substring(0, 8);
        IV = IV.Substring(0, 8);
        SymmetricAlgorithm sa;
        ICryptoTransform ict;
        MemoryStream ms;
        CryptoStream cs;
        byte[] byt;
        try
        {
            sa = new DESCryptoServiceProvider();
            sa.Key = Encoding.UTF8.GetBytes(key);
            sa.IV = Encoding.UTF8.GetBytes(IV);
            ict = sa.CreateDecryptor();
            byt = Convert.FromBase64String(encryptedValue);
            ms = new MemoryStream();
            cs = new CryptoStream(ms, ict, CryptoStreamMode.Write);
            cs.Write(byt, 0, byt.Length);
            cs.FlushFinalBlock();
            cs.Close();
            //return Encoding.UTF8.GetString(ms.ToArray());
            return UnicodeEncoding.Unicode.GetString(ms.ToArray()); //转码
        }
        catch (System.Exception)
        {
            return "";
        }
    }
}
