using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class Test : MonoBehaviour
{

	Text text;

	// Use this for initialization
	void Start()
	{

		text = GameObject.Find("Text").transform.GetComponent<Text>();

		print("This is new test.cs!");

		text.text += "Hello, world!\n";

		//S2 s2 = new S2();
		//print("AAAAAAAA  " + s2.s2_x);

		//s2.Set_s2_x(100);
		//print("BBBBBBBBBB  " + s2.s2_x);


		//print("CCCCCCCCCC  "+ S1.Instance.testListCap.Count);
		//S1.Instance.testListCap.Add(new Pos());
		//S1.Instance.testListCap.Add(new Pos(1,1));
		//S1.Instance.testListCap.Add(new Pos(2,2));
		//print("CCCCCCCCCC  " + S1.Instance.testListCap.Count);
		//S1.Instance.testListCap.Add(new Pos(3, 3));
		//print("CCCCCCCCCC  " + S1.Instance.testListCap.Count);
		//S1.Instance.testListCap.Add(new Pos(4, 4));
		//print("CCCCCCCCCC  " + S1.Instance.testListCap.Count+"  ----->  "+ S1.Instance.testListCap[4].x);


		//StringBuilder strBuilder=new StringBuilder(40);

		//print(strBuilder.Length);

		//strBuilder.Append("This is Test String.");
		//strBuilder.Append(" --by Lugiyan");

		//string str = strBuilder.ToString();
		//print(strBuilder.Length + "  _________  " + str);

		//strBuilder.Length = 0;
		//str = strBuilder.ToString();
		//print(strBuilder.Length + "  _________  " + str);


		//A a_bl = new A();

		//print(a_bl.strrInA);


		string textAES = "This is the string before encrypted. = ?-- 测试1208"; //明文

        string keys = "qxgamebylugiyan1";//密钥,128位            

        byte[] encryptBytes = AESEncryption.AESEncrypt(textAES, keys);

        //将加密后的密文转换为Base64编码，以便显示，可以查看下结果

        print("原始字符串：" + textAES);
		print("Aes加密字符串：" + Convert.ToBase64String(encryptBytes));

        //解密
        byte[] decryptBytes = AESEncryption.AESDecrypt(encryptBytes, keys);

        //将解密后的结果转换为字符串,也可以将该步骤封装在解密算法中
        string result = Encoding.UTF8.GetString(decryptBytes);

		print("Aes解码字符串：" + result);



		print("==================================");



        string strT = "This is the string before encrypted. = ?-- 测试1208";
		//print("原始字符串：" + strT);
		//strT = AesHelper.EncryptString(strT);
		//print("Aes加密字符串：" + strT);
		//strT = AesHelper.DecryptString(strT);
		//print("Aes解码字符串：" + strT);

		strT = "http://t16.manager.com/api/select_server.php?s_id=0&ac=init_account&game_id=1&ditch_name=16&cm=0&system_type=0&plat_user_name=点击输入帐号";
		print(strT);

		strT = RSAUtil.DESEncrypt(strT);
		print(strT);
		//strT = RSAUtil.DESDecrypt(strT);
		//print(strT);


		//string cet = "ODGCfQf^PaN\KBbBggonx1JIuPHLcs5ErA0yARRR+xkr29vXuLQJPT1ERBnlPjI2MtxWtSTv2DSkmFKFSQKUA53Kp56t0l0MwigI92MALbXw/njFnYV+lp4GWtU6NrmTvkF7JkY9L3E92HrR7gEk4Co46If/xqzLSP2Uf/KzmJM9IwZuFyteQ7YwnATz3qfP1I1CgOlBgVkW7/2W5J4=";
		System.Random rand = new System.Random();

		//print("============>" + rand.Next(1, 1));

		int num;  //字符最大分成多少段
		if (strT.Length < 1)
		{
			Debug.Log("错误，加密地址为空");
			return;
		}
		else if (strT.Length < 2) num = 1;
		else if (strT.Length < 4) num = 2;
		else num = rand.Next(4, 7);

		List<StringBuilder> strList = new List<StringBuilder>();

		for (int i = 0; i < num; i++)
			strList.Add(new StringBuilder(GetRandomString(rand.Next(4, 8), true, true)));

		// 字符列表排序，对应加密后的切割字符顺序
		strList.Sort((StringBuilder sb1, StringBuilder sb2) => sb1.ToString().CompareTo(sb2.ToString()));

		int _index = 0, _totel = 0;
		for (int i = 0; i < strList.Count; i++)
		{

			_totel = rand.Next(1, strT.Length / num);

			if (_index + _totel >= strT.Length || i == strList.Count - 1)   // 当剩余字符串不够切割 或者 最后字符串切割有剩余时，都全部赋值
			{
				strList[i].Append(strT.Substring(_index));
				break;
			}
			else
			{
				strList[i].Append(strT.Substring(_index, _totel));
			}
			_index += _totel;
		}

		print("======================================");

		StringBuilder margeStr = new StringBuilder();
		foreach (var val in strList) { 
			print(val);
			margeStr.Append(val.ToString().Split(new Char[] { '=' }, 2)[1]);
		}

		print("合并后加密的字符串：" + margeStr.ToString());

		print("解码字符串：" +RSAUtil.DESDecrypt( margeStr.ToString()));

	}

	///<summary>
	///生成随机字符串 
	///</summary>
	///<param name="length">目标字符串的长度</param>
	///<param name="useNum">是否包含数字，</param>
	///<param name="useLow">是否包含小写字母，</param>
	///<param name="useUpp">是否包含大写字母，</param>
	///<param name="useSpe">是否包含特殊字符，</param>
	///<param name="custom">要包含的自定义字符，直接输入要包含的字符列表</param>
	///<returns>指定长度的随机字符串</returns>
	public static string GetRandomString(int length = 2,
											bool useUpp = false,
											bool useLow = false,
											bool useNum = false,
											bool useSpe = false,
											string custom = "")
	{
		byte[] b = new byte[4];
		new System.Security.Cryptography.RNGCryptoServiceProvider().GetBytes(b);
		System.Random rand = new System.Random(BitConverter.ToInt32(b, 0));
		StringBuilder resultString = new StringBuilder(), tempStr = new StringBuilder();

		if (useUpp == true) { tempStr.Append("ABCDEFGHIJKLMNOPQRSTUVWXYZ"); }
		if (useLow == true) { tempStr.Append("abcdefghijklmnopqrstuvwxyz"); }
		if (useNum == true) { tempStr.Append("0123456789"); }
		if (useSpe == true) { tempStr.Append("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"); }
		tempStr.Append(custom);

		string strLib = tempStr.ToString();

		for (int i = 0; i < length; i++)
		{
			resultString.Append(strLib.Substring(rand.Next(0, tempStr.Length - 1), 1));
		}
		resultString.Append("=");
		return resultString.ToString();
	}



	// Update is called once per frame
	float timer = 0;
	void Update()
	{
		//timer += Time.deltaTime;
		//if (timer >= 1f) {
		//	timer = 0;
		//	print("AAAAA   " + Time.frameCount);
		//}

	}


	GameObject go;
	public void printText()
	{
		go = GameObject.Find("Sphere(Clone)");
		if (go != null)
		{
			text.text += go.name + " 对象存在\n";
		}
		else
		{
			text.text += "对象不存在\n";
		}


	}








}

