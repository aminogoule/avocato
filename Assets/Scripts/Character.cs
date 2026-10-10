using UnityEngine;

public class Character
{
    // Start is called before the first frame update
    public string Name;
    public int Exp;
    //default constructor
    public Character()
    {
        Reset();
    }
    //class constructor
    public Character(string name)
    {
        this.Name = name;
        this.Exp = 0;
    }
    public virtual void PrintStat()
    {
        Debug.Log($"Hero: {this.Name} - Exp {this.Exp}");
    }
    private void Reset()
    {
        this.Name = "Not Assigned";
        this.Exp = 0;
    }
}
public struct Weapon
{
    public string name;
    public int damage;
    public Weapon(string name, int damage)
    {
        this.name = name;
        this.damage = damage;
    }
    public void PrintWeaponStat()
    {
        Debug.Log($"Weapon {this.name}, has damage {this.damage}");
    }
}
