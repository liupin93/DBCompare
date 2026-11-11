using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DBCompare {
    // SQL tokens retain literal, identifier and comment contents. Only the gaps can be formatting.
    internal static class SqlDiff {
        sealed class Token { public int Start,End; public string Text; }
        static readonly Regex Number = new Regex(@"\G(?:0[xX][0-9a-fA-F]*|(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)",RegexOptions.Compiled);
        static bool Word(char c) { return Char.IsLetterOrDigit(c) || c=='_' || c=='#' || c=='@' || c=='$' || Char.GetUnicodeCategory(c)==System.Globalization.UnicodeCategory.NonSpacingMark; }
        static readonly HashSet<string> Operators=new HashSet<string>{"<>","!=","!<","!>","<=",">=","+=","-=","*=","/=","%=","&=","^=","|=","::","||","<<",">>"};
        static List<Token> Lex(string s) {
            var tokens=new List<Token>();
            for(int i=0;i<s.Length;) {
                int start=i;
                char c=s[i++], n=i<s.Length?s[i]:'\0';
                if(Char.IsWhiteSpace(c)) continue;
                if(c=='-' && n=='-') {
                    while(i<s.Length && s[i]!='\r' && s[i]!='\n') i++;
                } else if(c=='/' && n=='*') {
                    i++; int depth=1;
                    while(i<s.Length && depth>0) {
                        if(i+1<s.Length && s[i]=='/' && s[i+1]=='*') {depth++; i+=2;}
                        else if(i+1<s.Length && s[i]=='*' && s[i+1]=='/') {depth--; i+=2;}
                        else i++;
                    }
                } else if(c=='\'' || c=='"' || c=='[' || ((c=='N'||c=='n') && n=='\'')) {
                    if(c=='N'||c=='n') {c='\''; i++;}
                    char close=c=='['?']':c;
                    while(i<s.Length) if(s[i++]==close) {
                        if(i<s.Length && s[i]==close) i++;
                        else break;
                    }
                } else if((c>='0' && c<='9') || (c=='.' && n>='0' && n<='9')) {
                    i=start+Number.Match(s,start).Length;
                } else if(Word(c)) {
                    while(i<s.Length && Word(s[i])) i++;
                } else if(i<s.Length && Operators.Contains(s.Substring(start,2))) i++;
                tokens.Add(new Token{Start=start,End=i,Text=s.Substring(start,i-start)});
            }
            return tokens;
        }
        public static string Classify(string a,string b) {
            if(a==b) return Result.Same;
            if(a==null || b==null) return Result.Changed;
            return Lex(a).Select(x=>x.Text).SequenceEqual(Lex(b).Select(x=>x.Text),StringComparer.Ordinal) ? Result.Format : Result.Changed;
        }
        // Trim shared ends. Exact LCS for normal edit regions, bounded memory for large rewrites.
        static List<Tuple<int,int>> Matches(string[] a,string[] b) {
            var m=new List<Tuple<int,int>>();
            int head=0,endA=a.Length,endB=b.Length;
            while(head<endA && head<endB && a[head]==b[head]) { m.Add(Tuple.Create(head,head)); head++; }
            while(endA>head && endB>head && a[endA-1]==b[endB-1]) {endA--;endB--;}
            int na=endA-head,nb=endB-head;
            if(na>0 && nb>0 && (long)na*nb<=2000000) {
                var lengths=new int[na+1,nb+1];
                for(int i=na-1;i>=0;i--) for(int j=nb-1;j>=0;j--)
                    lengths[i,j]=a[i+head]==b[j+head]?1+lengths[i+1,j+1]:Math.Max(lengths[i+1,j],lengths[i,j+1]);
                for(int i=0,j=0;i<na && j<nb;) {
                    if(a[i+head]==b[j+head]) {m.Add(Tuple.Create(i+head,j+head));i++;j++;}
                    else if(lengths[i+1,j]>=lengths[i,j+1]) i++; else j++;
                }
            } else if(na>0 && nb>0) {
                var positions=new Dictionary<string,List<int>>(StringComparer.Ordinal);
                for(int j=head;j<endB;j++) {
                    List<int> list;
                    if(!positions.TryGetValue(b[j],out list)) positions[b[j]]=list=new List<int>();
                    list.Add(j);
                }
                int next=head;
                for(int i=head;i<endA && next<endB;i++) {
                    List<int> list;
                    if(!positions.TryGetValue(a[i],out list)) continue;
                    int p=list.BinarySearch(next);
                    if(p<0) p=~p;
                    if(p<list.Count) {int j=list[p];m.Add(Tuple.Create(i,j));next=j+1;}
                }
            }
            while(endA<a.Length) m.Add(Tuple.Create(endA++,endB++));
            return m;
        }
        public sealed class Piece { public string Text; public byte Tint; }
        public sealed class Line {
            public int Number;
            public string Text;
            public string Ending="";
            public List<Piece> Parts=new List<Piece>();
            public byte Level { get {return Parts.Count==0?(byte)0:Parts.Max(p=>p.Tint);} }
        }
        public sealed class Pair { public Line A,B; public bool Different {get {return A==null || B==null || A.Text!=B.Text || A.Level+B.Level>0;}} }
        static void Mark(byte[] colors,int start,int end,byte color) { for(int i=start;i<end;i++) colors[i]=color; }
        static List<Line> Lines(string s,byte[] colors,bool exists) {
            var lines=new List<Line>();
            if(!exists) return lines;
            int i=0;
            do {
                int start=i;
                var line=new Line{Number=lines.Count+1};
                while(i<s.Length && s[i]!='\r' && s[i]!='\n') {
                    int p=i++;
                    while(i<s.Length && s[i]!='\r' && s[i]!='\n' && colors[i]==colors[p]) i++;
                    line.Parts.Add(new Piece{Text=s.Substring(p,i-p),Tint=colors[p]});
                }
                line.Text=s.Substring(start,i-start);
                lines.Add(line);
                if(i==s.Length) break;
                int endingStart=i;
                byte newline=colors[i];
                if(s[i++]=='\r' && i<s.Length && s[i]=='\n') newline=Math.Max(newline,colors[i++]);
                line.Ending=s.Substring(endingStart,i-endingStart);
                if(newline>0) line.Parts.Add(new Piece{Text="↵",Tint=newline});
            } while(i<=s.Length);
            return lines;
        }
        static void Gap(string a,int ai,int az,byte[] ac,string b,int bi,int bz,byte[] bc) {
            while(ai<az && bi<bz && a[ai]==b[bi]) {ac[ai++]=0;bc[bi++]=0;}
            while(ai<az && bi<bz && a[az-1]==b[bz-1]) {ac[--az]=0;bc[--bz]=0;}
            Mark(ac,ai,az,1);Mark(bc,bi,bz,1);
        }
        static void CompareRegion(string a,List<Token> at,int lastA,int nextA,byte[] ac,string b,List<Token> bt,int lastB,int nextB,byte[] bc) {
            int ai=lastA<0?0:at[lastA].End,az=nextA==at.Count?a.Length:at[nextA].Start;
            int bi=lastB<0?0:bt[lastB].End,bz=nextB==bt.Count?b.Length:bt[nextB].Start;
            int insertedA=nextA-lastA-1,insertedB=nextB-lastB-1;
            if(insertedA==0 && insertedB==0) {
                Gap(a,ai,az,ac,b,bi,bz,bc);
            } else if(insertedA>0 && insertedB>0) {
                Gap(a,ai,at[lastA+1].Start,ac,b,bi,bt[lastB+1].Start,bc);
                Gap(a,at[nextA-1].End,az,ac,b,bt[nextB-1].End,bz,bc);
                if(insertedA==insertedB) for(int i=1;i<=insertedA;i++) {
                    var x=at[lastA+i];var y=bt[lastB+i];
                    if(i>1) Gap(a,at[lastA+i-1].End,x.Start,ac,b,bt[lastB+i-1].End,y.Start,bc);
                    // Highlight the actual edit within a replaced identifier, value or literal.
                    int l=x.Start,r=y.Start,le=x.End,re=y.End;
                    while(l<le && r<re && a[l]==b[r]) {ac[l++]=0;bc[r++]=0;}
                    while(l<le && r<re && a[le-1]==b[re-1]) {ac[--le]=0;bc[--re]=0;}
                }
            } else {
                // Preserve whitespace shared with the unchanged side of an insertion or deletion.
                while(ai<az && bi<bz && a[ai]==b[bi] && Char.IsWhiteSpace(a[ai])) {ac[ai++]=0;bc[bi++]=0;}
                while(ai<az && bi<bz && a[az-1]==b[bz-1] && Char.IsWhiteSpace(a[az-1])) {ac[--az]=0;bc[--bz]=0;}
            }
        }
        public static List<Pair> Detail(string left,string right) {
            string a=left??"", b=right??"";
            byte[] ac=Enumerable.Repeat((byte)2,a.Length).ToArray(),bc=Enumerable.Repeat((byte)2,b.Length).ToArray();
            if(left!=null && right!=null) {
                var at=Lex(a); var bt=Lex(b);
                int lastA=-1,lastB=-1;
                foreach(var p in Matches(at.Select(t=>t.Text).ToArray(),bt.Select(t=>t.Text).ToArray())) {
                    var x=at[p.Item1]; var y=bt[p.Item2];
                    CompareRegion(a,at,lastA,p.Item1,ac,b,bt,lastB,p.Item2,bc);
                    Mark(ac,x.Start,x.End,0);Mark(bc,y.Start,y.End,0);
                    lastA=p.Item1;lastB=p.Item2;
                }
                CompareRegion(a,at,lastA,at.Count,ac,b,bt,lastB,bt.Count,bc);
            }
            var al=Lines(a,ac,left!=null);var bl=Lines(b,bc,right!=null);
            var pairs=new List<Pair>();
            int l=0,r=0;
            var anchors=Matches(al.Select(x=>x.Text.Trim()).ToArray(),bl.Select(x=>x.Text.Trim()).ToArray());
            anchors.Add(Tuple.Create(al.Count,bl.Count));
            foreach(var anchor in anchors) {
                while(l<anchor.Item1 || r<anchor.Item2) pairs.Add(new Pair{A=l<anchor.Item1?al[l++]:null,B=r<anchor.Item2?bl[r++]:null});
                if(l<al.Count && r<bl.Count) pairs.Add(new Pair{A=al[l++],B=bl[r++]});
            }
            // Repeated tokens can match across an inserted line. An exact aligned line is unchanged.
            foreach(var pair in pairs) {
                if(pair.A==null || pair.B==null || pair.A.Text!=pair.B.Text || pair.A.Ending!=pair.B.Ending) continue;
                if(pair.A.Level==0 && pair.B.Level==0) continue;
                pair.A.Parts.Clear();pair.B.Parts.Clear();
                var unchanged=new Piece{Text=pair.A.Text};
                pair.A.Parts.Add(unchanged);pair.B.Parts.Add(unchanged);
            }
            return pairs;
        }
    }
}
