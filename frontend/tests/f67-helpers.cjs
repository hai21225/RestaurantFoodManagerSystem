const fs=require('node:fs'),vm=require('node:vm'),path=require('node:path');
const context=vm.createContext({window:{},TextEncoder});
for(const name of ['admin-session','account-session','order-session','payment-session']) vm.runInContext(fs.readFileSync(path.join(__dirname,'../js/common',name+'.js'),'utf8'),context);
const copy=x=>JSON.parse(JSON.stringify(x));
function storage(){const data=new Map();return {getItem:k=>data.get(k)||null,setItem:(k,v)=>data.set(k,v),removeItem:k=>data.delete(k)};}
function auth(role='Admin'){const a={role,userId:9,getUser:()=>({userId:a.userId,roles:[a.role]}),getToken:()=> 'test-token',hasRole:(...roles)=>roles.includes(a.role),updateUser:()=>{}};return a;}
module.exports={context,...context.window.restaurantAdmin,...context.window.restaurantAccount,PosSession:context.window.restaurantPos.OrderSession,PaymentSession:context.window.restaurantPayments.PaymentSession,copy,storage,auth};
